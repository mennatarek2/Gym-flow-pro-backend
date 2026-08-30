namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Activities;
using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.CallSheet;
using GMS.Application.DTOs.Dashboard;
using GMS.Application.DTOs.Debtors;
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
    private readonly IReportsService _reports;
    private readonly IDebtorsService _debtors;
    private readonly ICheckinService _checkins;
    private readonly IGymOccupancyService _occupancy;
    private readonly ISessionBookingService _sessions;
    private readonly ICallSheetService _callSheet;
    private readonly ITenantSettingsService _settings;

    public DashboardService(
        GymFlowProDbContext db,
        IReportsService reports,
        IDebtorsService debtors,
        ICheckinService checkins,
        IGymOccupancyService occupancy,
        ISessionBookingService sessions,
        ICallSheetService callSheet,
        ITenantSettingsService settings)
    {
        _db = db;
        _reports = reports;
        _debtors = debtors;
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
                    var todaySales = await _reports.GetSalesReportAsync(tenantId, today, today);
                    if (todaySales.IsSuccess && todaySales.Data != null)
                        dto.Today.RevenueToday = todaySales.Data.CashInTotal;
                    else
                        AddIssue(dto.DataIssues, "financial", "today_sales_unavailable");
                }
                catch
                {
                    AddIssue(dto.DataIssues, "financial", "today_sales_unavailable");
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
        SalesReportDto? sales = null;
        RefundsReportDto? refunds = null;

        try
        {
            var salesResult = await _reports.GetSalesReportAsync(tenantId, from, to);
            if (salesResult.IsSuccess)
                sales = salesResult.Data;
            else
                AddIssue(issues, "financial", "sales_unavailable");

            var refundsResult = await _reports.GetRefundsReportAsync(tenantId, from, to);
            if (refundsResult.IsSuccess)
                refunds = refundsResult.Data;
            else
                AddIssue(issues, "financial", "refunds_unavailable");
        }
        catch
        {
            AddIssue(issues, "financial", "reports_unavailable");
        }

        if (sales == null && refunds == null)
            return null;

        var breakdown = await BuildRevenueBreakdownAsync(tenantId, from, to, ct);
        var cashTrend = (sales?.Days ?? new List<SalesReportDayDto>())
            .Select(day => new DashboardTrendPointDto { Date = day.Date, Value = day.CashIn })
            .ToList();

        decimal outstanding = 0m;
        try
        {
            var debtors = await _debtors.GetSummaryAsync(tenantId);
            outstanding = debtors.IsSuccess ? debtors.Data?.TotalOutstanding ?? 0m : 0m;
            if (!debtors.IsSuccess)
                AddIssue(issues, "financial", "outstanding_unavailable");
        }
        catch
        {
            AddIssue(issues, "financial", "outstanding_unavailable");
        }

        decimal? expenses = null;
        if (canViewExpenses)
        {
            try
            {
                expenses = await _db.CashExpenses.AsNoTracking()
                    .Where(expense => expense.TenantId == tenantId
                                      && expense.Status == "posted"
                                      && expense.ExpenseDate >= from
                                      && expense.ExpenseDate <= to)
                    .Select(expense => expense.Amount)
                    .SumAsync(ct);
            }
            catch
            {
                AddIssue(issues, "financial", "expenses_unavailable");
            }
        }
        var cashCollected = sales?.CashInTotal ?? 0m;
        var refundsTotal = refunds?.Total ?? sales?.CashRefundsTotal ?? 0m;
        var netCash = cashCollected - refundsTotal;

        return new DashboardFinancialDto
        {
            CashCollected = cashCollected,
            Refunds = refundsTotal,
            Outstanding = outstanding,
            Expenses = expenses,
            NetProfit = expenses.HasValue ? netCash - expenses.Value : null,
            ProfitMargin = expenses.HasValue && cashCollected > 0
                ? decimal.Round((netCash - expenses.Value) / cashCollected * 100m, 2)
                : null,
            Breakdown = breakdown,
            CashTrend = cashTrend
        };
    }

    private async Task<List<DashboardRevenueBreakdownDto>> BuildRevenueBreakdownAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var range = MembershipOperational.CairoInclusiveRangeUtc(from, to);
        var payments = await _db.PaymentTransactions.AsNoTracking()
            .Where(p => p.TenantId == tenantId
                        && p.Status == "success"
                        && p.Amount > 0
                        && p.PaidAtUtc >= range.UtcStart
                        && p.PaidAtUtc < range.UtcEndExclusive)
            .Select(p => new { p.Amount, p.SaleId, p.MembershipId })
            .ToListAsync(ct);

        var saleIds = payments.Where(p => p.SaleId.HasValue).Select(p => p.SaleId!.Value).Distinct().ToList();
        var lineTypes = await _db.SaleLines.AsNoTracking()
            .Where(line => line.TenantId == tenantId && saleIds.Contains(line.SaleId))
            .Select(line => new { line.SaleId, line.LineType })
            .ToListAsync(ct);
        var membershipIds = payments.Where(p => p.MembershipId.HasValue)
            .Select(p => p.MembershipId!.Value).Distinct().ToList();
        var memberships = await _db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && membershipIds.Contains(m.Id))
            .Select(m => new { m.Id, m.LastRenewalDate, m.PlanTransitionMode })
            .ToDictionaryAsync(m => m.Id, ct);

        var totals = new Dictionary<string, (decimal Amount, int Count)>(StringComparer.Ordinal)
        {
            ["memberships"] = (0m, 0),
            ["renewals"] = (0m, 0),
            ["products"] = (0m, 0),
            ["classes"] = (0m, 0)
        };

        foreach (var payment in payments)
        {
            var types = lineTypes.Where(line => line.SaleId == payment.SaleId)
                .Select(line => line.LineType.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
            string key;
            if (payment.MembershipId.HasValue
                && memberships.TryGetValue(payment.MembershipId.Value, out var membership)
                && IsRenewal(membership.LastRenewalDate, membership.PlanTransitionMode))
            {
                key = "renewals";
            }
            else if (types.Contains("membership") || types.Contains("trial"))
            {
                key = "memberships";
            }
            else if (types.Contains("retail") || types.Contains("product"))
            {
                key = "products";
            }
            else if (types.Contains("drop_in") || types.Contains("day_pass") || types.Contains("class"))
            {
                key = "classes";
            }
            else
            {
                continue;
            }

            var current = totals[key];
            totals[key] = (current.Amount + payment.Amount, current.Count + 1);
        }

        return totals.Select(pair => new DashboardRevenueBreakdownDto
        {
            Key = pair.Key,
            Amount = pair.Value.Amount,
            Count = pair.Value.Count
        }).ToList();
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

        if (canFinance)
        {
            Result<DebtorsSummaryDto>? debtors = null;
            try
            {
                debtors = await _debtors.GetSummaryAsync(tenantId);
            }
            catch
            {
                AddIssue(dto.DataIssues, "attention", "debtors_unavailable");
            }

            if (debtors?.IsSuccess == true && debtors.Data != null)
                dto.Attention.Items.Add(new DashboardAttentionItemDto
                {
                    Key = "outstanding_payments",
                    Count = debtors.Data.DebtorCount,
                    Amount = debtors.Data.TotalOutstanding
                });
            else if (debtors != null)
                AddIssue(dto.DataIssues, "attention", "debtors_unavailable");
        }

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
