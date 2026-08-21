namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Analytics;

/// <summary>
/// Analytics service for dashboard KPIs (from pre-computed snapshots).
/// All data comes from gym_analytics_snapshots for performance.
/// </summary>
public interface IAnalyticsService
{
    /// <summary>
    /// Get dashboard overview from latest snapshot.
    /// Falls back to real-time calculation if no snapshot exists.
    /// </summary>
    Task<Result<DashboardOverviewDto>> GetDashboardOverviewAsync(Guid tenantId);

    /// <summary>
    /// Get revenue chart for last N months.
    /// </summary>
    Task<Result<RevenueChartDto>> GetRevenueChartAsync(Guid tenantId, int months = 6);

    /// <summary>
    /// Get attendance heatmap (7 days × 24 hours) from last 30 days.
    /// </summary>
    Task<Result<AttendanceHeatmapDto>> GetAttendanceHeatmapAsync(Guid tenantId);

    /// <summary>
    /// Get member status breakdown (active, expired, frozen, cancelled).
    /// </summary>
    Task<Result<MemberStatusPieDto>> GetMemberStatusBreakdownAsync(Guid tenantId);

    /// <summary>
    /// Get member invitation funnel with conversion rate.
    /// </summary>
    Task<Result<InvitationFunnelDto>> GetInvitationFunnelAsync(Guid tenantId);

    /// <summary>
    /// Get the trial funnel (Issued/Converted/Expired/ConversionRate) for trials issued in the
    /// given month (format "yyyy-MM").
    /// </summary>
    Task<Result<TrialAnalyticsDto>> GetTrialAnalyticsAsync(Guid tenantId, string month);
}
