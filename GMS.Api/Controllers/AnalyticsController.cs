namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Analytics;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// REST API controller for analytics dashboards.
/// All data from pre-computed snapshots for performance.
/// </summary>
[Route("api/analytics")]
[Authorize]
public class AnalyticsController : BaseApiController
{
    private readonly IAnalyticsService _analyticsService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        IAnalyticsService analyticsService,
        ITenantContext tenantContext,
        ILogger<AnalyticsController> logger)
    {
        _analyticsService = analyticsService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get dashboard overview KPIs.
    /// GET /api/analytics/overview
    /// </summary>
    [HttpGet("overview")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(DashboardOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOverview()
    {
        var result = await _analyticsService.GetDashboardOverviewAsync(_tenantContext.TenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>
    /// Get revenue chart for last N months.
    /// GET /api/analytics/revenue?months=6
    /// </summary>
    [HttpGet("revenue")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(RevenueChartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRevenueChart([FromQuery] int months = 6)
    {
        if (months < 1 || months > 36)
            return BadRequest("Months must be between 1 and 36");

        var result = await _analyticsService.GetRevenueChartAsync(_tenantContext.TenantId, months);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>
    /// Get attendance heatmap (7 days × 24 hours).
    /// GET /api/analytics/heatmap
    /// </summary>
    [HttpGet("heatmap")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(AttendanceHeatmapDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAttendanceHeatmap()
    {
        var result = await _analyticsService.GetAttendanceHeatmapAsync(_tenantContext.TenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>
    /// Get member status breakdown.
    /// GET /api/analytics/members-status
    /// </summary>
    [HttpGet("members-status")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(MemberStatusPieDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMemberStatusBreakdown()
    {
        var result = await _analyticsService.GetMemberStatusBreakdownAsync(_tenantContext.TenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>
    /// Get member invitation funnel.
    /// GET /api/analytics/invitations
    /// </summary>
    [HttpGet("invitations")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(InvitationFunnelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInvitationFunnel()
    {
        var result = await _analyticsService.GetInvitationFunnelAsync(_tenantContext.TenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>
    /// Get the trial funnel (Issued/Converted/Expired/ConversionRate) for a given month.
    /// GET /api/analytics/trials?month=2026-07
    /// </summary>
    [HttpGet("trials")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(GMS.Application.DTOs.Analytics.TrialAnalyticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTrialAnalytics([FromQuery] string month)
    {
        if (string.IsNullOrWhiteSpace(month))
            return BadRequest("month query parameter is required (yyyy-MM) / معامل الشهر مطلوب (yyyy-MM)");

        var result = await _analyticsService.GetTrialAnalyticsAsync(_tenantContext.TenantId, month);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }
}
