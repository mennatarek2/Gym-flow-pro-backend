namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Imports;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Excel/CSV bulk-import pipeline for members + current memberships: upload, column mapping,
/// dry-run validation, execution, and time-boxed rollback.
/// </summary>
[Route("api/imports")]
[Authorize]
[FeatureFlag("imports")]
public class ImportsController : BaseApiController
{
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    private readonly IImportService _importService;
    private readonly ITenantContext _tenantContext;

    public ImportsController(IImportService importService, ITenantContext tenantContext)
    {
        _importService = importService;
        _tenantContext = tenantContext;
    }

    /// <summary>POST /api/imports (multipart, ≤5MB, ≤10,000 rows)</summary>
    [HttpPost]
    [HasPermission(Permissions.SettingsManage)]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Problem(detail: "No file was uploaded / لم يتم رفع أي ملف", statusCode: StatusCodes.Status400BadRequest);

        await using var stream = file.OpenReadStream();
        var result = await _importService.UploadAsync(
            stream, file.FileName, file.ContentType, GetUserId(), _tenantContext.TenantId);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>POST /api/imports/{id}/mapping</summary>
    [HttpPost("{id:guid}/mapping")]
    [HasPermission(Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetMapping(Guid id, [FromBody] ColumnMapRequest mapping)
    {
        var result = await _importService.SetMappingAsync(id, mapping, _tenantContext.TenantId);
        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>GET /api/imports/{id}</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id)
    {
        var dto = await _importService.GetAsync(id, _tenantContext.TenantId);
        if (dto == null)
            return NotFound(new { message = "Import batch not found / دفعة الاستيراد غير موجودة" });

        return Ok(dto);
    }

    /// <summary>GET /api/imports/{id}/errors.csv</summary>
    [HttpGet("{id:guid}/errors.csv")]
    [HasPermission(Permissions.SettingsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetErrorsCsv(Guid id)
    {
        var csv = await _importService.GetErrorsCsvAsync(id, _tenantContext.TenantId);
        if (csv == null)
            return NotFound(new { message = "Import batch not found / دفعة الاستيراد غير موجودة" });

        return File(csv, "text/csv", "import-errors.csv");
    }

    /// <summary>POST /api/imports/{id}/execute</summary>
    [HttpPost("{id:guid}/execute")]
    [HasPermission(Permissions.SettingsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Execute(Guid id)
    {
        var result = await _importService.EnqueueExecuteAsync(id, _tenantContext.TenantId);
        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(new { message = "Import execution started / بدأ تنفيذ الاستيراد" });
    }

    /// <summary>POST /api/imports/{id}/rollback — Manager+ only, within 7 days of completion.</summary>
    [HttpPost("{id:guid}/rollback")]
    [Authorize(Policy = "ManagerOrAbove")]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rollback(Guid id)
    {
        var result = await _importService.RollbackAsync(id, GetUserId(), _tenantContext.TenantId);
        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>POST /api/imports/{id}/create-plans — creates plans the import referenced but couldn't match.</summary>
    [HttpPost("{id:guid}/create-plans")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePlans(Guid id, [FromBody] CreatePlansFromImportRequest request)
    {
        var result = await _importService.CreateMissingPlansAsync(id, request.Plans, _tenantContext.TenantId);
        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>GET /api/imports/template.xlsx</summary>
    [HttpGet("template.xlsx")]
    [HasPermission(Permissions.SettingsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetTemplate()
    {
        var bytes = _importService.BuildTemplateXlsx();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "import-template.xlsx");
    }

    // ── Helpers ──

    private Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private IActionResult ProblemFromResult(string error)
    {
        var (code, message) = SplitReason(error);

        var statusCode = code switch
        {
            var c when c == ImportFailureReasons.BatchNotFound => StatusCodes.Status404NotFound,
            var c when c == ImportFailureReasons.InvalidStatus => StatusCodes.Status400BadRequest,
            var c when c == ImportFailureReasons.FileTooLarge => StatusCodes.Status400BadRequest,
            var c when c == ImportFailureReasons.TooManyRows => StatusCodes.Status400BadRequest,
            var c when c == ImportFailureReasons.UnsupportedFileType => StatusCodes.Status400BadRequest,
            var c when c == ImportFailureReasons.RollbackWindowExpired => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(detail: message, statusCode: statusCode, title: code);
    }

    private static (string Code, string Message) SplitReason(string error)
    {
        var separatorIndex = error.IndexOf('|');
        return separatorIndex < 0 ? ("ERROR", error) : (error[..separatorIndex], error[(separatorIndex + 1)..]);
    }
}
