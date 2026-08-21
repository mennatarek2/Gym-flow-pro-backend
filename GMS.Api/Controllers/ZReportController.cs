namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.Interfaces;
using GMS.Application.DTOs.ZReports;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Daily closing Z-Report: read access to a tenant's immutable per-day snapshot, its PDF, and a
/// manager-only regeneration escape hatch.
/// </summary>
[Route("api/reports/z")]
[Authorize]
public class ZReportController : BaseApiController
{
    private readonly IZReportService _zReportService;
    private readonly ITenantContext _tenantContext;

    public ZReportController(IZReportService zReportService, ITenantContext tenantContext)
    {
        _zReportService = zReportService;
        _tenantContext = tenantContext;
    }

    /// <summary>List shift closing reports by Cairo OpenedAt range.</summary>
    [HttpGet("shifts")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(ShiftZReportListDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListShiftClosings(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        var result = await _zReportService.ListShiftClosingsAsync(_tenantContext.TenantId, from, to);
        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);
        return Ok(result.Data);
    }

    /// <summary>Shift closing Z-Report from shift-linked transactions. Closed cash figures are frozen on the Shift.</summary>
    [HttpGet("shifts/{shiftId:guid}")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(ShiftZReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShiftClosing(Guid shiftId)
    {
        var result = await _zReportService.GetShiftClosingAsync(_tenantContext.TenantId, shiftId);
        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);
        return Ok(result.Data);
    }

    [HttpGet("shifts/{shiftId:guid}/pdf")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShiftClosingPdf(Guid shiftId)
    {
        var result = await _zReportService.GetShiftClosingPdfAsync(_tenantContext.TenantId, shiftId);
        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);
        return File(result.Data!, "application/pdf", $"z-report-shift-{shiftId:N}.pdf");
    }

    /// <summary>GET /api/reports/z/{date} — daily Cairo snapshot (nightly job). Unchanged.</summary>
    [HttpGet("{date}")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetZReport(DateOnly date)
    {
        var result = await _zReportService.GetAsync(_tenantContext.TenantId, date);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>GET /api/reports/z/{date}/pdf</summary>
    [HttpGet("{date}/pdf")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetZReportPdf(DateOnly date)
    {
        var result = await _zReportService.GetPdfBytesAsync(_tenantContext.TenantId, date);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return File(result.Data!, "application/pdf", $"z-report-{date:yyyy-MM-dd}.pdf");
    }

    /// <summary>POST /api/reports/z/{date}/regenerate</summary>
    [HttpPost("{date}/regenerate")]
    [Authorize(Policy = "ManagerOrAbove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Regenerate(DateOnly date)
    {
        var result = await _zReportService.RegenerateAsync(_tenantContext.TenantId, date, GetUserId());

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    /// <summary>IZReportService encodes a machine-readable reason as a "CODE|message" prefix on failure.</summary>
    private IActionResult ProblemFromResult(string error)
    {
        var (code, message) = SplitReason(error);

        var statusCode = code switch
        {
            var c when c == ZReportFailureReasons.NotFound => StatusCodes.Status404NotFound,
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
