namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>HR Phase 6: employee documents. Files are private HR data — FileUrl is never exposed;
/// the only way to read bytes is the protected "/file" download action below.</summary>
[Route("api/hr")]
[Authorize]
[FeatureFlag("hr")]
public class HrEmployeeDocumentsController : BaseApiController
{
    private readonly IEmployeeDocumentService _documents;
    private readonly IEmployeeService _employees;
    private readonly ITenantContext _tenantContext;

    public HrEmployeeDocumentsController(IEmployeeDocumentService documents, IEmployeeService employees, ITenantContext tenantContext)
    {
        _documents = documents;
        _employees = employees;
        _tenantContext = tenantContext;
    }

    [HttpGet("employee-documents")]
    [HasPermission(Permissions.HrDocumentsView)]
    [ProducesResponseType(typeof(List<EmployeeDocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAll([FromQuery] string? expiryStatus = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _documents.ListAllAsync(_tenantContext.TenantId, expiryStatus);
        return Ok(result.Data);
    }

    [HttpGet("employees/{employeeId:guid}/documents")]
    [HasPermission(Permissions.HrDocumentsView)]
    [ProducesResponseType(typeof(List<EmployeeDocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid employeeId)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _documents.ListAsync(_tenantContext.TenantId, employeeId);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("employees/{employeeId:guid}/documents")]
    [HasPermission(Permissions.HrDocumentsManage)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType(typeof(EmployeeDocumentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(Guid employeeId, IFormFile? file, [FromForm] string documentType, [FromForm] DateOnly? issueDate, [FromForm] DateOnly? expiryDate, [FromForm] string? notes)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded / لم يتم رفع ملف" });
        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { error = "File must be ≤ 10MB / الملف يجب ألا يتجاوز 10 ميجا" });

        var contentType = (file.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        var isAllowed = contentType is "image/jpeg" or "image/jpg" or "image/png" or "image/webp" or "application/pdf";
        if (!isAllowed)
            return BadRequest(new { error = "Only JPEG/PNG/WebP images or PDF files / صور JPEG أو PNG أو WebP أو ملفات PDF فقط" });

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        await using var stream = file.OpenReadStream();
        var result = await _documents.UploadAsync(_tenantContext.TenantId, employeeId, stream, file.FileName, contentType,
            new CreateEmployeeDocumentRequest { DocumentType = documentType, IssueDate = issueDate, ExpiryDate = expiryDate, Notes = notes },
            actorAppUserId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(List), new { employeeId }, result.Data);
    }

    [HttpDelete("employee-documents/{id:guid}")]
    [HasPermission(Permissions.HrDocumentsManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var result = await _documents.DeleteAsync(_tenantContext.TenantId, id, actorAppUserId);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return NoContent();
    }

    [HttpGet("employee-documents/{id:guid}/file")]
    [HasPermission(Permissions.HrDocumentsView)]
    public async Task<IActionResult> Download(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _documents.DownloadAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });

        return File(result.Data.Bytes, string.IsNullOrWhiteSpace(result.Data.ContentType) ? "application/octet-stream" : result.Data.ContentType, result.Data.FileName);
    }

    // ── Self-service ──

    [HttpGet("employees/me/documents")]
    [ProducesResponseType(typeof(List<EmployeeDocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var employeeId = await ResolveOwnEmployeeIdAsync();
        if (employeeId == null)
            return Forbid();

        var result = await _documents.ListAsync(_tenantContext.TenantId, employeeId.Value);
        return Ok(result.Data);
    }

    [HttpGet("employee-documents/me/{id:guid}/file")]
    public async Task<IActionResult> DownloadMine(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var employeeId = await ResolveOwnEmployeeIdAsync();
        if (employeeId == null)
            return Forbid();

        var result = await _documents.DownloadAsync(_tenantContext.TenantId, id, employeeId);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });

        return File(result.Data.Bytes, string.IsNullOrWhiteSpace(result.Data.ContentType) ? "application/octet-stream" : result.Data.ContentType, result.Data.FileName);
    }

    private Task<Guid?> ResolveOwnEmployeeIdAsync()
    {
        var identityUserId = GetIdentityUserId();
        return identityUserId == Guid.Empty
            ? Task.FromResult<Guid?>(null)
            : _employees.ResolveEmployeeIdForCallerAsync(_tenantContext.TenantId, identityUserId);
    }

    private Task<Guid?> ResolveActingAppUserIdAsync()
    {
        var identityUserId = GetIdentityUserId();
        return identityUserId == Guid.Empty
            ? Task.FromResult<Guid?>(null)
            : _employees.ResolveAppUserIdForCallerAsync(_tenantContext.TenantId, identityUserId);
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
