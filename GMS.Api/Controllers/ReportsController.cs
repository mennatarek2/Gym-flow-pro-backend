namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Analytics;
using GMS.Application.DTOs.Reports;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// REST API controller for detailed reports.
/// Real-time queries from source tables.
/// </summary>
[Route("api/reports")]
[Authorize]
public class ReportsController : BaseApiController
{
    private readonly IReportsService _reportsService;
    private readonly IProfitabilityService _profitabilityService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        IReportsService reportsService,
        IProfitabilityService profitabilityService,
        ITenantContext tenantContext,
        ILogger<ReportsController> logger)
    {
        _reportsService = reportsService;
        _profitabilityService = profitabilityService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get attendance summary for date range.
    /// GET /api/reports/attendance-summary?from=2026-05-01&to=2026-05-31
    /// </summary>
    [HttpGet("attendance-summary")]
    [HasAnyPermission(Permissions.MembersView, Permissions.AttendanceView)]
    [ProducesResponseType(typeof(List<AttendanceSummaryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAttendanceSummary(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        if (from > to)
            return BadRequest("From date must be before To date");

        var result = await _reportsService.GetAttendanceSummaryAsync(_tenantContext.TenantId, from, to);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>
    /// Get revenue details for date range.
    /// GET /api/reports/revenue-detail?from=2026-05-01&to=2026-05-31&method=cash
    /// </summary>
    [HttpGet("revenue-detail")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(List<RevenueDetailItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRevenueDetail(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? method = null)
    {
        if (from > to)
            return BadRequest("From date must be before To date");

        var result = await _reportsService.GetRevenueDetailAsync(_tenantContext.TenantId, from, to, method);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>
    /// Get peak attendance hours (top 5).
    /// GET /api/reports/peak-hours
    /// </summary>
    [HttpGet("peak-hours")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(List<PeakHourItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPeakHours()
    {
        var result = await _reportsService.GetPeakHoursAsync(_tenantContext.TenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>
    /// Get member retention rate.
    /// GET /api/reports/member-retention
    /// </summary>
    [HttpGet("member-retention")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(MemberRetentionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMemberRetention()
    {
        var result = await _reportsService.GetMemberRetentionAsync(_tenantContext.TenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>Operational payment-collection detail from PaymentTransaction (PaidAtUtc, Cairo days).</summary>
    [HttpGet("sales")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(SalesReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSales(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? method = null,
        [FromQuery] Guid? staffId = null,
        [FromQuery] string? type = null)
    {
        var result = await _reportsService.GetSalesReportAsync(
            _tenantContext.TenantId, from, to, method, staffId, type);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>Operational executed-refund detail by ExecutedAt (Cairo days).</summary>
    [HttpGet("refunds")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(RefundsReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRefunds(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? method = null,
        [FromQuery] Guid? staffId = null,
        [FromQuery] string? buyer = null)
    {
        var result = await _reportsService.GetRefundsReportAsync(
            _tenantContext.TenantId, from, to, method, staffId, buyer);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>
    /// Memberships that started in the Cairo range. Type = new|renewal from PlanTransitionMode / LastRenewalDate.
    /// Operational membership/payment detail. Canonical revenue is provided by /profitability.
    /// </summary>
    [HttpGet("memberships")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(MembershipsReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberships(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? planId = null,
        [FromQuery] Guid? staffId = null,
        [FromQuery] string? type = null)
    {
        var result = await _reportsService.GetMembershipsReportAsync(
            _tenantContext.TenantId, from, to, planId, staffId, type);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>Retail SaleLine sold in the Cairo range. Full refunds drop out. Not warehouse analytics.</summary>
    [HttpGet("products")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(ProductsReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProducts(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? productId = null,
        [FromQuery] Guid? staffId = null,
        [FromQuery] string? method = null)
    {
        var result = await _reportsService.GetProductsReportAsync(
            _tenantContext.TenantId, from, to, productId, staffId, method);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>Operational staff/shift detail. Payment totals here are not Owner Dashboard revenue or settled cash.</summary>
    [HttpGet("staff-shifts")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(StaffShiftsReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStaffShifts(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? staffId = null,
        [FromQuery] Guid? shiftId = null)
    {
        var result = await _reportsService.GetStaffShiftsReportAsync(
            _tenantContext.TenantId, from, to, staffId, shiftId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>
    /// Canonical profitability and cash-flow report. Payment collections,
    /// revenue, COGS, expenses, AR, AP, and cash flow remain separate.
    /// </summary>
    [HttpGet("profitability")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(ProfitabilityDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfitability(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        var result = await _profitabilityService.GetAsync(
            _tenantContext.TenantId, from, to, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        Response.Headers["X-Financial-Calculation-Version"] = result.Data!.CalculationVersion;
        return Ok(result.Data);
    }

    /// <summary>Canonical settled cash inflows and actual cash/bank outflows.</summary>
    [HttpGet("cash-flow")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(CashFlowDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCashFlow(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        var result = await _profitabilityService.GetAsync(
            _tenantContext.TenantId, from, to, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        var report = result.Data!;
        Response.Headers["X-Financial-Calculation-Version"] = report.CalculationVersion;
        return Ok(new CashFlowDto
        {
            CalculationVersion = report.CalculationVersion,
            From = report.From,
            To = report.To,
            Collections = report.Collections,
            SettledCashInflow = report.SettledCashInflow,
            SettledCashAvailable = report.SettledCashAvailable,
            CashRefunds = report.CashRefunds,
            OperatingExpenseCashOutflows = report.OperatingExpenses,
            PayrollCashDisbursements = report.PayrollCashDisbursements,
            SupplierCashPayments = report.SupplierCashPayments,
            CashOutflows = report.CashOutflows,
            NetCashFlow = report.NetCashFlow,
            CashFlowAvailable = report.CashFlowAvailable,
            SupplierCashPaymentsAvailable = report.SupplierCashPaymentsAvailable,
            PayrollCoverageStatus = report.PayrollCoverageStatus,
            DataIssues = report.DataIssues
        });
    }

    /// <summary>
    /// Compatibility alias for consumers that need the canonical reconciliation boundary
    /// without adopting the profitability presentation name.
    /// </summary>
    [HttpGet("financial-reconciliation")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(ProfitabilityDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFinancialReconciliation(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        var result = await _profitabilityService.GetAsync(
            _tenantContext.TenantId, from, to, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        Response.Headers["X-Financial-Calculation-Version"] = result.Data!.CalculationVersion;
        return Ok(result.Data);
    }

    /// <summary>
    /// Backfills immutable sale-line COGS only from traceable sale stock movements.
    /// Missing or ambiguous cost evidence is returned as an exception and remains unavailable.
    /// </summary>
    [HttpPost("profitability/backfill-cogs")]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(CogsBackfillDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> BackfillCogs(CancellationToken ct)
    {
        var result = await _profitabilityService.BackfillCogsAsync(_tenantContext.TenantId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }
}
