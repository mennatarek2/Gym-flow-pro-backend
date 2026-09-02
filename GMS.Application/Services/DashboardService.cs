namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Activities;
using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.CallSheet;
using GMS.Application.DTOs.Dashboard;
using GMS.Application.DTOs.Reports;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

public sealed class DashboardService : IDashboardService
{
    private const int DefaultInactivityDays = 30;
    private const int RenewalWindowDays = 7;
    private const decimal NearFullThreshold = 0.8m;

    private readonly GymFlowProDbContext _db;
    private readonly IProfitabilityService _profitability;
    private readonly ICheckinService _checkins;
    private readonly IGymOccupancyService _occupancy;
    private readonly ISessionBookingService _sessions;
    private readonly ICallSheetService _callSheet;
    private readonly ITenantSettingsService _settings;

    public DashboardService(
        GymFlowProDbContext db,
        IProfitabilityService profitability,
        ICheckinService checkins,
        IGymOccupancyService occupancy,
        ISessionBookingService sessions,
        ICallSheetService callSheet,
        ITenantSettingsService settings)
    {
        _db = db;
        _profitability = profitability;
        _checkins = checkins;
        _occupancy = occupancy;
        _sessions = sessions;
        _callSheet = callSheet;
        _settings = settings;
    }

    public async Task<Result<DashboardOverviewDto>> GetOverviewAsync(
        Guid tenantId,
        DashboardQuery query,
        DashboardAccessContext access,
        CancellationToken ct = default)
    {
        var today = MembershipOperational.TodayCairo();
        var period = ResolvePeriod(query, today);
        var dto = new DashboardOverviewDto
        {
            Period = new DashboardPeriodDto
            {
                Key = period.Key,
                From = period.From,
                To = period.To
            },
            Operations = new DashboardOperationsDto
            {
                NearFullThresholdPercent = (int)(NearFullThreshold * 100)
            }
        };

        var canMembers = access.Has(Permissions.MembersView);
        var canAttendance = canMembers
            || access.Has(Permissions.AttendanceView)
            || access.Has(Permissions.CheckinManual);
        var canClasses = canMembers || access.Has(Permissions.ClassesView);
        var canFinance = access.Has(Permissions.ReportsFinancialView);

        if (canMembers)
        {
            dto.Business = await BuildBusinessAsync(tenantId, period.From, period.To, today, dto.DataIssues, ct);
            dto.Today.ActiveMembers = dto.Business.ActiveMembers;
        }

        if (canFinance)
        {
            dto.Financial = await BuildFinancialAsync(
                tenantId,
                period.From,
                period.To,
                access.Has(Permissions.ReportsExpensesView),
                dto.DataIssues,
                ct);
            if (dto.Financial != null)
            {
                dto.Today.Outstanding = dto.Financial.Outstanding;
                try
                {
                    var todayFinancial = await _profitability.GetAsync(tenantId, today, today, ct);
                    if (todayFinancial.IsSuccess && todayFinancial.Data != null)
                        dto.Today.RevenueToday = todayFinancial.Data.Revenue;
                    else
                        AddIssue(dto.DataIssues, "financial", "today_revenue_unavailable");
                }
                catch
                {
                    AddIssue(dto.DataIssues, "financial", "today_revenue_unavailable");
                }
            }
        }

        if (canAttendance)
            await AddAttendanceAsync(tenantId, today, dto, ct);

        if (canClasses)
            await AddSessionsAsync(tenantId, today, access, dto, ct);

        if (canMembers || canFinance)
            await AddAttentionAsync(tenantId, today, dto, canMembers, canFinance, ct);

        await AddQuickActionsAsync(tenantId, access, dto);
        return Result<DashboardOverviewDto>.Success(dto);
    }

    private async Task<DashboardBusinessDto> BuildBusinessAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        DateOnly today,
        List<DashboardDataIssueDto> issues,
        CancellationToken ct)
    {
        var active = await _db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                        && m.Status == "active"
                        && m.StartDate <= today
                        && m.EndDate >= today)
            .Select(m => m.MemberId)
            .Distinct()
            .CountAsync(ct);

        var expired = await _db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                        && (m.Status == "expired"
                            || (m.Status == "active" && m.EndDate < today)))
            .Select(m => m.MemberId)
            .Distinct()
            .CountAsync(ct);

        var inactivityDays = await GetInactivityDaysAsync(tenantId, ct);
        var attendanceCutoff = MembershipOperational.CairoInclusiveRangeUtc(
            today.AddDays(-inactivityDays + 1), today).UtcStart;
        var inactive = await _db.GymMembers.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.IsActive)
            .CountAsync(m => !_db.GymAttendances.Any(a =>
                a.TenantId == tenantId
                && a.MemberId == m.Id
                && a.CheckInAtUtc >= attendanceCutoff), ct);

        var memberships = await _db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                        && m.Status != "cancelled"
                        && m.StartDate >= from
                        && m.StartDate <= to)
            .Select(m => new { m.LastRenewalDate, m.PlanTransitionMode })
            .ToListAsync(ct);

        var renewals = memberships.Count(m => IsRenewal(m.LastRenewalDate, m.PlanTransitionMode));
        var trialsEndingSoon = await _db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                        && m.Member != null
                        && m.Member.IsTrial
                        && m.Member.TrialOutcome == "active_trial"
                        && m.EndDate >= today
                        && m.EndDate <= today.AddDays(RenewalWindowDays))
            .Select(m => m.MemberId)
            .Distinct()
            .CountAsync(ct);
        return new DashboardBusinessDto
        {
            ActiveMembers = active,
            Expired = expired,
            Inactive = inactive,
            NewMembers = memberships.Count - renewals,
            Renewals = renewals,
            TrialsEndingSoon = trialsEndingSoon
        };
    }

    private async Task<DashboardFinancialDto?> BuildFinancialAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        bool canViewExpenses,
        List<DashboardDataIssueDto> issues,
        CancellationToken ct)
    {
        ProfitabilityDto? financial = null;

        try
        {
            var profitability = await _profitability.GetAsync(tenantId, from, to, ct);
            if (profitability.IsSuccess)
                financial = profitability.Data;
            else
                AddIssue(issues, "financial", "profitability_unavailable");
        }
        catch
        {
            AddIssue(issues, "financial", "profitability_unavailable");
        }

        if (financial == null)
            return null;

        foreach (var issue in financial.DataIssues)
            AddIssue(issues, "financial", issue);

        decimal? expenses = canViewExpenses ? financial.OperatingExpenses : null;

        return new DashboardFinancialDto
        {
            CashCollected = financial.Collections,
            CalculationVersion = financial.CalculationVersion,
            Collections = financial.Collections,
            SettledCashInflow = financial.SettledCashInflow,
            SettledCashAvailable = financial.SettledCashAvailable,
            Revenue = financial.Revenue,
            RevenueAdjustments = financial.RevenueAdjustments,
            Refunds = financial.Refunds,
            CashRefunds = financial.CashRefunds,
            CreditRefunds = financial.CreditRefunds,
            Outstanding = financial.AccountsReceivable,
            Expenses = expenses,
            Cogs = financial.Cogs,
            GrossProfit = financial.GrossProfit,
            PayrollExpense = financial.PayrollExpense,
            OperatingExpenses = expenses,
            NetProfit = canViewExpenses ? financial.NetProfit : null,
            NetProfitAvailable = canViewExpenses && financial.NetProfitAvailable,
            ProfitMargin = canViewExpenses ? financial.ProfitMargin : null,
            CashOutflows = financial.CashOutflows,
            NetCashFlow = financial.NetCashFlow,
            CashFlowAvailable = financial.CashFlowAvailable,
            SupplierCashPaymentsAvailable = financial.SupplierCashPaymentsAvailable,
            AccountsReceivable = financial.AccountsReceivable,
            AccountsReceivableCount = financial.AccountsReceivableCount,
            AccountsPayable = financial.AccountsPayable,
            CogsAvailable = financial.CogsAvailable,
            PayrollAvailable = financial.PayrollAvailable,
            PayrollCoverageStatus = financial.PayrollCoverageStatus,
            FinancialDataIssues = financial.DataIssues,
            TrustStates = financial.TrustStates,
            Breakdown = financial.RevenueBreakdown
                .Select(item => new DashboardRevenueBreakdownDto
                {
                    Key = item.Key,
                    Amount = item.Amount,
                    Count = item.Count
                })
                .ToList(),
            RevenueTrend = financial.RevenueTrend
                .Select(item => new DashboardTrendPointDto
                {
                    Date = item.Date,
                    Value = item.Value
                })
                .ToList()
        };
    }

    private async Task AddAttendanceAsync(
        Guid tenantId,
        DateOnly today,
        DashboardOverviewDto dto,
        CancellationToken ct)
    {
        try
        {
            var attendance = await _checkins.GetTodayAttendanceAsync(tenantId);
            if (!attendance.IsSuccess || attendance.Data == null)
            {
                AddIssue(dto.DataIssues, "attendance", "today_unavailable");
            }
            else
            {
                dto.Today.CheckinsToday = attendance.Data.Count;
                dto.Today.TodayAttendance = attendance.Data.Count;
                dto.Operations.CheckinsToday = attendance.Data.Count;
            }

            var occupancy = await _occupancy.GetOccupancyAsync(tenantId, ct);
            if (!occupancy.IsSuccess || occupancy.Data == null)
            {
                AddIssue(dto.DataIssues, "attendance", "occupancy_unavailable");
            }
            else
            {
                dto.Today.CurrentlyInside = occupancy.Data.CurrentlyInside;
                dto.Operations.CurrentlyInside = occupancy.Data.CurrentlyInside;
                dto.Operations.MaxCapacity = occupancy.Data.MaxCapacity;
                dto.Operations.AvailableCapacity = occupancy.Data.Available;
                dto.Operations.OccupancyPercent = occupancy.Data.OccupancyPercent;
            }

            var range = MembershipOperational.CairoInclusiveRangeUtc(today.AddDays(-6), today);
            var checkinTimes = await _db.GymAttendances.AsNoTracking()
                .Where(a => a.TenantId == tenantId
                            && a.CheckInAtUtc >= range.UtcStart
                            && a.CheckInAtUtc < range.UtcEndExclusive)
                .Select(a => a.CheckInAtUtc)
                .ToListAsync(ct);
            dto.Operations.AttendanceTrend = checkinTimes
                .GroupBy(MembershipOperational.ToCairoDate)
                .Select(group => new DashboardTrendPointDto
                {
                    Date = group.Key,
                    Value = group.Count()
                })
                .OrderBy(point => point.Date)
                .ToList();
        }
        catch
        {
            AddIssue(dto.DataIssues, "attendance", "attendance_unavailable");
        }
    }

    private async Task AddSessionsAsync(
        Guid tenantId,
        DateOnly today,
        DashboardAccessContext access,
        DashboardOverviewDto dto,
        CancellationToken ct)
    {
        var allSessions = new List<SessionDto>();
        for (var date = today; date <= today.AddDays(7); date = date.AddDays(1))
        {
            try
            {
                var result = await _sessions.GetSessionsByDateAsync(tenantId, date, ct);
                if (result.IsSuccess && result.Data != null)
                    allSessions.AddRange(result.Data);
                else
                    AddIssue(dto.DataIssues, "classes", "sessions_unavailable");
            }
            catch
            {
                AddIssue(dto.DataIssues, "classes", "sessions_unavailable");
            }
        }

        var now = DateTime.UtcNow;
        var sessionDtos = allSessions
            .Select(session => new DashboardSessionDto
            {
                Id = session.Id,
                ActivityName = session.ActivityName,
                StartsAtUtc = session.StartsAtUtc,
                EndsAtUtc = session.EndsAtUtc,
                Capacity = session.Capacity,
                BookedCount = session.BookedCount,
                CheckedInCount = session.CheckedInCount,
                RemainingCapacity = session.RemainingCapacity,
                IsNearlyFull = session.Capacity > 0
                    && session.BookedCount / (decimal)session.Capacity >= NearFullThreshold,
                IsMine = access.UserId.HasValue && session.CoachUserId == access.UserId,
                CoachName = session.CoachName
            })
            .ToList();
        if (string.Equals(access.Role, "Trainer", StringComparison.OrdinalIgnoreCase))
            sessionDtos = sessionDtos.Where(session => session.IsMine).ToList();

        var todaySessions = sessionDtos
            .Where(session => MembershipOperational.ToCairoDate(session.StartsAtUtc) == today)
            .ToList();
        dto.Operations.Sessions = sessionDtos;
        dto.Today.TodayClasses = todaySessions.Count;
        dto.Today.UpcomingBookings = todaySessions.Sum(session => session.BookedCount);
        dto.Today.MyUpcomingClasses = sessionDtos.Count(session =>
            session.IsMine && session.EndsAtUtc > now);
        var nextMine = sessionDtos.FirstOrDefault(session =>
            session.IsMine && session.EndsAtUtc > now);
        if (nextMine != null)
        {
            dto.Today.ClassCapacityBooked = nextMine.BookedCount;
            dto.Today.ClassCapacityTotal = nextMine.Capacity;
        }

        if (string.Equals(access.Role, "Trainer", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var session in sessionDtos.Where(session => session.IsMine))
            {
                var detail = await _sessions.GetSessionDetailAsync(tenantId, session.Id, ct);
                if (!detail.IsSuccess || detail.Data == null)
                {
                    AddIssue(dto.DataIssues, "classes", "roster_unavailable");
                    continue;
                }

                session.Bookings = detail.Data.Bookings.Select(booking => new DashboardBookingDto
                {
                    Id = booking.Id,
                    MemberId = booking.MemberId,
                    Name = booking.MemberName,
                    Phone = booking.MemberPhone ?? booking.GuestPhone,
                    Status = booking.Status,
                    CheckedIn = booking.CheckedInAtUtc.HasValue
                }).ToList();
            }
        }
    }

    private async Task AddAttentionAsync(
        Guid tenantId,
        DateOnly today,
        DashboardOverviewDto dto,
        bool canMembers,
        bool canFinance,
        CancellationToken ct)
    {
        if (canMembers)
        {
            Result<List<CallSheetEntryDto>>? expiring = null;
            try
            {
                expiring = await _callSheet.GetExpiringAsync(tenantId, RenewalWindowDays);
            }
            catch
            {
                AddIssue(dto.DataIssues, "attention", "renewals_unavailable");
            }

            if (expiring?.IsSuccess == true && expiring.Data != null)
            {
                dto.Today.RenewalsDueSoon = expiring.Data.Count;
                dto.Attention.Items.Add(new DashboardAttentionItemDto
                {
                    Key = "renewals_due",
                    Count = expiring.Data.Count
                });
            }
            else if (expiring != null)
                AddIssue(dto.DataIssues, "attention", "renewals_unavailable");

            var inactive = dto.Business?.Inactive ?? 0;
            if (inactive > 0)
                dto.Attention.Items.Add(new DashboardAttentionItemDto
                {
                    Key = "inactive_members",
                    Count = inactive
                });

            if (dto.Business?.TrialsEndingSoon > 0)
                dto.Attention.Items.Add(new DashboardAttentionItemDto
                {
                    Key = "trials_ending_soon",
                    Count = dto.Business.TrialsEndingSoon
                });
        }

        if (canFinance && dto.Financial?.AccountsReceivableCount > 0)
            dto.Attention.Items.Add(new DashboardAttentionItemDto
            {
                Key = "outstanding_payments",
                Count = dto.Financial.AccountsReceivableCount,
                Amount = dto.Financial.AccountsReceivable
            });

        var nearFull = dto.Operations.Sessions.Count(session => session.IsNearlyFull);
        if (nearFull > 0)
            dto.Attention.Items.Add(new DashboardAttentionItemDto
            {
                Key = "classes_near_full",
                Count = nearFull
            });
    }

    private async Task AddQuickActionsAsync(
        Guid tenantId,
        DashboardAccessContext access,
        DashboardOverviewDto dto)
    {
        Result<QuickActionsSettingsDto>? configured = null;
        try
        {
            configured = await _settings.GetQuickActionsAsync(tenantId);
        }
        catch
        {
            // Defaults below are safe when optional settings are unavailable.
        }
        var keys = configured?.IsSuccess == true && configured.Data?.Keys != null
            ? configured.Data.Keys
            : QuickActionKeys.DefaultKeys.ToList();
        if (keys.SequenceEqual(QuickActionKeys.DefaultKeys, StringComparer.Ordinal))
        {
            keys = access.Role.ToLowerInvariant() switch
            {
                "trainer" => new List<string>
                {
                    QuickActionKeys.CheckinMember, QuickActionKeys.ViewClasses
                },
                "receptionist" => new List<string>
                {
                    QuickActionKeys.NewMember, QuickActionKeys.Checkin,
                    QuickActionKeys.BookClass, QuickActionKeys.NewSale
                },
                "manager" => new List<string>
                {
                    QuickActionKeys.NewMember, QuickActionKeys.Checkin,
                    QuickActionKeys.NewSale, QuickActionKeys.FreezeMembership
                },
                _ => keys
            };
        }

        foreach (var key in keys)
        {
            if (!IsQuickActionAvailable(key, access))
                continue;
            dto.QuickActions.Add(new DashboardQuickActionDto { Key = key });
        }
    }

    private static bool IsQuickActionAvailable(string key, DashboardAccessContext access) =>
        key switch
        {
            QuickActionKeys.NewMember => access.Has(Permissions.MembersCreate),
            QuickActionKeys.Checkin => access.Has(Permissions.CheckinManual),
            QuickActionKeys.NewSale or QuickActionKeys.CollectPayment =>
                access.Has(Permissions.SalesSell),
            QuickActionKeys.NewTrial => access.Has(Permissions.MembersCreate),
            QuickActionKeys.SendDebtorReminder => access.Has(Permissions.MembersView),
            QuickActionKeys.NewRefund => access.Has(Permissions.PaymentsRefundRequest),
            QuickActionKeys.FreezeMembership => access.Has(Permissions.MembershipsFreeze),
            QuickActionKeys.BookClass or QuickActionKeys.ViewClasses =>
                access.Has(Permissions.ClassesView) || access.Has(Permissions.MembersView),
            QuickActionKeys.CheckinMember =>
                access.Has(Permissions.CheckinManual) || access.Has(Permissions.AttendanceView),
            _ => access.Role is "Owner" or "Manager"
        };

    private static (string Key, DateOnly From, DateOnly To) ResolvePeriod(
        DashboardQuery query,
        DateOnly today)
    {
        var requested = (query.Period ?? "month").Trim().ToLowerInvariant();
        if (query.From.HasValue && query.To.HasValue && query.From <= query.To)
            return ("custom", query.From.Value, query.To.Value);

        return requested switch
        {
            "today" => ("today", today, today),
            "week" => ("week", today.AddDays(-6), today),
            "last_month" => LastMonth(today),
            "year" => ("year", new DateOnly(today.Year, 1, 1), today),
            "last_year" => ("last_year", new DateOnly(today.Year - 1, 1, 1),
                new DateOnly(today.Year - 1, 12, 31)),
            _ => ("month", new DateOnly(today.Year, today.Month, 1), today)
        };
    }

    private static (string Key, DateOnly From, DateOnly To) LastMonth(DateOnly today)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        return ("last_month", first, first.AddMonths(1).AddDays(-1));
    }

    private static bool IsRenewal(DateTime? lastRenewalDate, string? transitionMode) =>
        lastRenewalDate.HasValue || !string.IsNullOrWhiteSpace(transitionMode);

    private async Task<int> GetInactivityDaysAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await _db.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Settings)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(settings))
            return DefaultInactivityDays;

        try
        {
            using var document = JsonDocument.Parse(settings);
            if (document.RootElement.TryGetProperty(
                    TenantSettingsKeys.DashboardInactivityDays, out var value)
                && value.TryGetInt32(out var days))
            {
                return Math.Clamp(days, 1, 365);
            }
        }
        catch (JsonException)
        {
            // Invalid optional settings must not prevent the dashboard from loading.
        }

        return DefaultInactivityDays;
    }

    private static void AddIssue(List<DashboardDataIssueDto> issues, string section, string code)
    {
        if (issues.Any(issue => issue.Section == section && issue.Code == code))
            return;
        issues.Add(new DashboardDataIssueDto { Section = section, Code = code });
    }
}
