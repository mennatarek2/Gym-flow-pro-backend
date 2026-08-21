namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Analytics;
using GMS.Application.DTOs.Reports;

/// <summary>
/// Reports service for detailed analytics (real-time from source tables).
/// Detailed queries for business intelligence and reporting.
/// </summary>
public interface IReportsService
{
    /// <summary>
    /// Get attendance summary for date range.
    /// </summary>
    Task<Result<List<AttendanceSummaryItemDto>>> GetAttendanceSummaryAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate);

    /// <summary>
    /// Get revenue details for date range, optionally filtered by payment method.
    /// </summary>
    Task<Result<List<RevenueDetailItemDto>>> GetRevenueDetailAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, string? paymentMethod = null);

    /// <summary>
    /// Get top 5 peak hours based on checkins.
    /// </summary>
    Task<Result<List<PeakHourItemDto>>> GetPeakHoursAsync(Guid tenantId);

    /// <summary>
    /// Get member retention rate (% of members who renewed).
    /// </summary>
    Task<Result<MemberRetentionDto>> GetMemberRetentionAsync(Guid tenantId);

    Task<Result<SalesReportDto>> GetSalesReportAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        string? paymentMethod = null, Guid? staffId = null, string? saleType = null);

    Task<Result<RefundsReportDto>> GetRefundsReportAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        string? method = null, Guid? staffId = null, string? buyer = null);

    Task<Result<MembershipsReportDto>> GetMembershipsReportAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        Guid? planId = null, Guid? staffId = null, string? type = null);

    Task<Result<ProductsReportDto>> GetProductsReportAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        Guid? productId = null, Guid? staffId = null, string? method = null);

    Task<Result<StaffShiftsReportDto>> GetStaffShiftsReportAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        Guid? staffId = null, Guid? shiftId = null);
}
