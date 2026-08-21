namespace GMS.Application.Services;

using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Analytics;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Analytics service implementation.
/// Queries pre-computed snapshots for dashboard KPIs.
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(GymFlowProDbContext dbContext, ILogger<AnalyticsService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<DashboardOverviewDto>> GetDashboardOverviewAsync(Guid tenantId)
    {
        try
        {
            // Get latest snapshot for today
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var snapshot = await _dbContext.AnalyticsSnapshots
                .Where(s => s.TenantId == tenantId && s.SnapshotDate == today)
                .OrderByDescending(s => s.CreatedAtUtc)
                .FirstOrDefaultAsync();

            // If no snapshot for today, calculate real-time
            if (snapshot == null)
            {
                _logger.LogInformation("No snapshot for today, calculating real-time data for tenant {TenantId}", tenantId);
                return await CalculateRealtimeDashboardAsync(tenantId);
            }

            var dto = new DashboardOverviewDto
            {
                ActiveMembers = snapshot.ActiveMembers,
                ExpiredMembers = snapshot.ExpiredMembers,
                NewMembersThisMonth = snapshot.NewMembersThisMonth,
                RevenueThisMonth = snapshot.RevenueThisMonth,
                CheckinsToday = snapshot.CheckinsToday,
                CheckinsThisWeek = snapshot.CheckinsThisWeek,
                SnapshotTimeUtc = snapshot.CreatedAtUtc
            };

            return Result<DashboardOverviewDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard overview for tenant {TenantId}", tenantId);
            return Result<DashboardOverviewDto>.Failure("Failed to get dashboard overview");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<RevenueChartDto>> GetRevenueChartAsync(Guid tenantId, int months = 6)
    {
        try
        {
            var startDate = DateTime.UtcNow.AddMonths(-months);

            // Year/Month GroupBy + new DateTime(...) in Select often fail SQL translation;
            // also Sum(Plan.Price * g.Count()) was wrong (multiplied count once per row).
            var payments = await _dbContext.Memberships
                .Where(m => m.TenantId == tenantId && m.PaymentDate >= startDate)
                .Select(m => new { m.PaymentDate, m.AmountPaid })
                .ToListAsync();

            var revenueByMonth = payments
                .Where(m => m.PaymentDate.HasValue)
                .GroupBy(m => new { m.PaymentDate!.Value.Year, m.PaymentDate.Value.Month })
                .Select(g => new
                {
                    Month = new DateTime(g.Key.Year, g.Key.Month, 1),
                    Revenue = g.Sum(m => m.AmountPaid)
                })
                .ToList();

            var labels = new List<string>();
            var values = new List<decimal>();

            var currentDate = DateTime.UtcNow.AddMonths(-months);
            for (int i = 0; i < months; i++)
            {
                labels.Add(currentDate.ToString("MMM"));
                var revenue = revenueByMonth
                    .FirstOrDefault(r => r.Month.Year == currentDate.Year && r.Month.Month == currentDate.Month)
                    ?.Revenue ?? 0;
                values.Add(revenue);
                currentDate = currentDate.AddMonths(1);
            }

            var dto = new RevenueChartDto { Labels = labels, Values = values };
            return Result<RevenueChartDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting revenue chart for tenant {TenantId}", tenantId);
            return Result<RevenueChartDto>.Failure("Failed to get revenue chart");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<AttendanceHeatmapDto>> GetAttendanceHeatmapAsync(Guid tenantId)
    {
        try
        {
            var heatmap = new AttendanceHeatmapDto();
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

            // DayOfWeek/Hour are not translated by the SQL Server provider inside GroupBy —
            // project timestamps server-side, then aggregate in memory (30 days of check-ins is fine).
            var checkInTimes = await _dbContext.GymAttendances
                .Where(a => a.TenantId == tenantId && a.CheckInAtUtc >= thirtyDaysAgo)
                .Select(a => a.CheckInAtUtc)
                .ToListAsync();

            var attendanceData = checkInTimes
                .GroupBy(t => new { DayOfWeek = (int)t.DayOfWeek, Hour = t.Hour })
                .Select(g => new { g.Key.DayOfWeek, g.Key.Hour, Count = g.Count() });

            foreach (var item in attendanceData)
            {
                // Convert Sunday (0) to (6), and shift others — Mon-first 7×24 grid
                int dayIndex = item.DayOfWeek == 0 ? 6 : item.DayOfWeek - 1;
                heatmap.Data[dayIndex][item.Hour] = item.Count;
            }

            return Result<AttendanceHeatmapDto>.Success(heatmap);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting attendance heatmap for tenant {TenantId}", tenantId);
            return Result<AttendanceHeatmapDto>.Failure("Failed to get attendance heatmap");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<MemberStatusPieDto>> GetMemberStatusBreakdownAsync(Guid tenantId)
    {
        try
        {
            var today = MembershipOperational.TodayCairo();

            // Count MEMBERS by operational membership (same rules as list filters), not raw membership rows.
            var members = await _dbContext.GymMembers
                .Where(m => m.TenantId == tenantId && m.IsActive)
                .Select(m => new
                {
                    m.Id,
                    Memberships = m.Memberships.Select(ms => new
                    {
                        ms.Status,
                        ms.StartDate,
                        ms.EndDate,
                        ms.CreatedAtUtc
                    }).ToList()
                })
                .ToListAsync();

            int active = 0, expired = 0, frozen = 0, cancelled = 0;
            foreach (var member in members)
            {
                var list = member.Memberships
                    .Select(ms => new Membership
                    {
                        Status = ms.Status,
                        StartDate = ms.StartDate,
                        EndDate = ms.EndDate,
                        CreatedAtUtc = ms.CreatedAtUtc
                    })
                    .ToList();

                var selected = MembershipOperational.SelectOperational(list, today);
                if (selected == null)
                    continue;

                var effective = MembershipOperational.GetEffectiveStatus(selected, today);
                switch (effective)
                {
                    case "active":
                    case "scheduled":
                        active++;
                        break;
                    case "expired":
                        expired++;
                        break;
                    case "frozen":
                        frozen++;
                        break;
                    case "cancelled":
                        cancelled++;
                        break;
                }
            }

            var dto = new MemberStatusPieDto
            {
                Active = active,
                Expired = expired,
                Frozen = frozen,
                Cancelled = cancelled
            };

            return Result<MemberStatusPieDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting member status breakdown for tenant {TenantId}", tenantId);
            return Result<MemberStatusPieDto>.Failure("Failed to get member status breakdown");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<InvitationFunnelDto>> GetInvitationFunnelAsync(Guid tenantId)
    {
        try
        {
            var rows = await _dbContext.MemberInvitations
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId)
                .Select(i => new
                {
                    i.InvitationType,
                    i.Status,
                    i.VisitedAtUtc,
                    i.ConvertedAtUtc,
                    i.ConvertedMemberId
                })
                .ToListAsync();

            InvitationTypeFunnelDto Slice(string type)
            {
                var subset = rows.Where(r =>
                    string.Equals(r.InvitationType, type, StringComparison.OrdinalIgnoreCase)
                    || (type == InvitationTypes.GuestPass
                        && string.IsNullOrWhiteSpace(r.InvitationType))).ToList();
                var sent = subset.Count;
                var visited = subset.Count(r => r.VisitedAtUtc.HasValue);
                var converted = subset.Count(r => r.ConvertedAtUtc.HasValue
                    || string.Equals(r.Status, InvitationStatuses.Converted, StringComparison.OrdinalIgnoreCase));
                return new InvitationTypeFunnelDto
                {
                    Sent = sent,
                    Visited = visited,
                    Converted = converted,
                    ConversionRate = sent > 0 ? (converted / (decimal)sent) * 100 : 0
                };
            }

            var guest = Slice(InvitationTypes.GuestPass);
            var referral = Slice(InvitationTypes.Referral);
            var product = rows.Where(r =>
                string.Equals(r.InvitationType, InvitationTypes.Invitation, StringComparison.OrdinalIgnoreCase)).ToList();

            var sent = product.Count;
            var converted = product.Count(r =>
                r.ConvertedAtUtc.HasValue
                || string.Equals(r.Status, InvitationStatuses.Converted, StringComparison.OrdinalIgnoreCase));
            var newCount = product.Count(r =>
                string.Equals(r.Status, InvitationStatuses.New, StringComparison.OrdinalIgnoreCase));
            var contacted = product.Count(r =>
                string.Equals(r.Status, InvitationStatuses.Contacted, StringComparison.OrdinalIgnoreCase));
            var interested = product.Count(r =>
                string.Equals(r.Status, InvitationStatuses.Interested, StringComparison.OrdinalIgnoreCase));
            var notInterested = product.Count(r =>
                string.Equals(r.Status, InvitationStatuses.NotInterested, StringComparison.OrdinalIgnoreCase));

            var cairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
            var today = MembershipOperational.TodayCairo();
            var monthStartLocal = new DateOnly(today.Year, today.Month, 1).ToDateTime(TimeOnly.MinValue);
            var monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(monthStartLocal, DateTimeKind.Unspecified), cairoTz);
            var nextMonthUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(monthStartLocal.AddMonths(1), DateTimeKind.Unspecified), cairoTz);

            var newMembersThisMonth = await _dbContext.GymMembers
                .CountAsync(m => m.TenantId == tenantId
                              && m.CreatedAtUtc >= monthStartUtc
                              && m.CreatedAtUtc < nextMonthUtc);

            var invitationConvertedThisMonth = await _dbContext.MemberInvitations
                .Where(i => i.TenantId == tenantId
                         && i.InvitationType == InvitationTypes.Invitation
                         && i.ConvertedAtUtc != null
                         && i.ConvertedAtUtc >= monthStartUtc
                         && i.ConvertedAtUtc < nextMonthUtc
                         && i.ConvertedMemberId != null)
                .Select(i => i.ConvertedMemberId!.Value)
                .Distinct()
                .CountAsync();

            var pct = newMembersThisMonth > 0
                ? (invitationConvertedThisMonth / (decimal)newMembersThisMonth) * 100
                : 0;

            var dto = new InvitationFunnelDto
            {
                Sent = sent,
                New = newCount,
                Contacted = contacted,
                Interested = interested,
                NotInterested = notInterested,
                Visited = contacted,
                Converted = converted,
                ConversionRate = sent > 0 ? (converted / (decimal)sent) * 100 : 0,
                GuestPass = guest,
                Referral = referral,
                NewMembersThisMonth = newMembersThisMonth,
                ReferralConvertedMembersThisMonth = invitationConvertedThisMonth,
                PercentNewMembersFromReferrals = Math.Round(pct, 2)
            };

            return Result<InvitationFunnelDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invitation funnel for tenant {TenantId}", tenantId);
            return Result<InvitationFunnelDto>.Failure("Failed to get invitation funnel");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<TrialAnalyticsDto>> GetTrialAnalyticsAsync(Guid tenantId, string month)
    {
        try
        {
            if (!DateTime.TryParseExact($"{month}-01", "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var monthStart))
            {
                return Result<TrialAnalyticsDto>.Failure("Invalid month format, expected yyyy-MM / صيغة الشهر غير صحيحة، يجب أن تكون بصيغة yyyy-MM");
            }

            var monthEnd = monthStart.AddMonths(1);

            // "Issued" is the cohort of trials whose Membership was created in this month;
            // Converted/Expired reflect each member's CURRENT TrialOutcome (which may have been
            // reached after the month ended).
            var trialMemberIds = await _dbContext.Memberships
                .Where(m => m.TenantId == tenantId
                         && m.Plan!.PlanType == "trial"
                         && m.CreatedAtUtc >= monthStart
                         && m.CreatedAtUtc < monthEnd)
                .Select(m => m.MemberId)
                .Distinct()
                .ToListAsync();

            var issued = trialMemberIds.Count;

            var converted = issued == 0 ? 0 : await _dbContext.GymMembers
                .CountAsync(m => trialMemberIds.Contains(m.Id) && m.TrialOutcome == "converted");

            var expired = issued == 0 ? 0 : await _dbContext.GymMembers
                .CountAsync(m => trialMemberIds.Contains(m.Id) && m.TrialOutcome == "expired");

            var conversionRate = issued > 0 ? Math.Round((converted / (decimal)issued) * 100, 2) : 0m;

            return Result<TrialAnalyticsDto>.Success(new TrialAnalyticsDto
            {
                Issued = issued,
                Converted = converted,
                Expired = expired,
                ConversionRate = conversionRate
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting trial analytics for tenant {TenantId}, month {Month}", tenantId, month);
            return Result<TrialAnalyticsDto>.Failure("Failed to get trial analytics / فشل جلب تحليلات التجربة المجانية");
        }
    }

    // Helper method for real-time calculation when no snapshot exists
    private async Task<Result<DashboardOverviewDto>> CalculateRealtimeDashboardAsync(Guid tenantId)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            var weekStart = today.AddDays(-(int)today.DayOfWeek + 1);

            var activeMembers = await _dbContext.Memberships
                .CountAsync(m => m.TenantId == tenantId && m.Status == "active");

            var expiredMembers = await _dbContext.Memberships
                .CountAsync(m => m.TenantId == tenantId && m.Status == "expired");

            var newThisMonth = await _dbContext.GymMembers
                .CountAsync(m => m.TenantId == tenantId && m.CreatedAtUtc.Date >= monthStart.ToDateTime(TimeOnly.MinValue));

            var revenueThisMonth = await _dbContext.Memberships
                .Where(m => m.TenantId == tenantId && m.PaymentDate.HasValue && 
                           m.PaymentDate.Value.Date >= monthStart.ToDateTime(TimeOnly.MinValue))
                .SumAsync(m => m.Plan!.Price);

            var checkinsToday = await _dbContext.GymAttendances
                .CountAsync(a => a.TenantId == tenantId && a.CheckInAtUtc.Date == today.ToDateTime(TimeOnly.MinValue));

            var checkinsThisWeek = await _dbContext.GymAttendances
                .CountAsync(a => a.TenantId == tenantId && a.CheckInAtUtc.Date >= weekStart.ToDateTime(TimeOnly.MinValue));

            var dto = new DashboardOverviewDto
            {
                ActiveMembers = activeMembers,
                ExpiredMembers = expiredMembers,
                NewMembersThisMonth = newThisMonth,
                RevenueThisMonth = revenueThisMonth,
                CheckinsToday = checkinsToday,
                CheckinsThisWeek = checkinsThisWeek,
                SnapshotTimeUtc = DateTime.UtcNow
            };

            return Result<DashboardOverviewDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating real-time dashboard for tenant {TenantId}", tenantId);
            return Result<DashboardOverviewDto>.Failure("Failed to calculate real-time dashboard");
        }
    }
}
