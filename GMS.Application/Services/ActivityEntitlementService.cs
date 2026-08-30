namespace GMS.Application.Services;

using System.Linq;
using Microsoft.EntityFrameworkCore;
using GMS.Application.DTOs.Members;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Resolves a member's activity eligibility (plan entitlement) and enforces class quotas.
/// Single source of truth for quota counting — booking, member APIs and DTOs all read through here.
/// Quota-consuming statuses: booked, checked_in, cancelled_late, no_show. A timely "cancelled"
/// frees the credit; everything else consumes it.
/// </summary>
public class ActivityEntitlementService : IActivityEntitlementService
{
    public const string EligibilityIncluded = "included";
    public const string EligibilityLimited = "limited";
    public const string EligibilityUnlimited = "unlimited";
    public const string EligibilityDropIn = "drop_in";
    public const string EligibilityNotEntitled = "not_entitled";

    private readonly GymFlowProDbContext _db;

    public ActivityEntitlementService(GymFlowProDbContext db) => _db = db;

    /// <summary>
    /// Resolve the entitlement covering this member for one activity today.
    /// Uses the covering (check-in-eligible) membership — never stale/operational rows.
    /// </summary>
    public async Task<ActivityEntitlement?> ResolveAsync(
        Guid tenantId, Guid memberId, Guid activityId, CancellationToken ct = default)
    {
        var today = MembershipOperational.TodayCairo();

        var memberships = await _db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.MemberId == memberId && !m.IsDeleted)
            .ToListAsync(ct);
        var covering = MembershipOperational.SelectCoveringToday(memberships, today);
        if (covering == null)
            return null;

        var entitlement = await _db.PlanEntitlements.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId
                                      && e.PlanId == covering.PlanId
                                      && e.ActivityId == activityId
                                      && !e.IsDeleted, ct);

        return new ActivityEntitlement(covering, entitlement);
    }

    /// <summary>
    /// Count of quota credits already consumed by this member against their covering membership,
    /// for the given activity. Limited-mode only; other modes return 0.
    /// Consumption is attributed to the covering membership active when the booking was made.
    /// </summary>
    public async Task<int> CountConsumedAsync(
        Guid tenantId, Guid memberId, Guid activityId, Membership coveringMembership, CancellationToken ct = default)
    {
        var quotaPeriod = await _db.PlanEntitlements.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && e.PlanId == coveringMembership.PlanId
                        && e.ActivityId == activityId
                        && !e.IsDeleted)
            .Select(e => e.QuotaPeriod)
            .FirstOrDefaultAsync(ct);

        return await CountConsumedForPeriodAsync(
            tenantId, memberId, activityId, coveringMembership, quotaPeriod, ct);
    }

    private async Task<int> CountConsumedForPeriodAsync(
        Guid tenantId,
        Guid memberId,
        Guid activityId,
        Membership coveringMembership,
        string? quotaPeriod,
        CancellationToken ct)
    {
        var (periodStartUtc, periodEndUtc) = QuotaPeriodUtcRange(coveringMembership, quotaPeriod);
        var sessionIds = _db.ActivitySessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && s.ActivityId == activityId
                        && s.StartsAtUtc >= periodStartUtc
                        && s.StartsAtUtc < periodEndUtc)
            .Select(s => s.Id);

        return await _db.ActivityBookings.AsNoTracking()
            .Where(b => b.TenantId == tenantId
                        && b.MemberId == memberId
                        && b.CoveringMembershipId == coveringMembership.Id
                        && sessionIds.Contains(b.SessionId)
                        && !b.IsDeleted
                        && (b.Status == ActivityBookingStatuses.Booked
                            || b.Status == ActivityBookingStatuses.CheckedIn
                            || b.Status == ActivityBookingStatuses.CancelledLate
                            || b.Status == ActivityBookingStatuses.NoShow))
            .CountAsync(ct);
    }

    private static (DateTime StartUtc, DateTime EndUtc) QuotaPeriodUtcRange(
        Membership membership, string? quotaPeriod)
    {
        var period = (quotaPeriod ?? string.Empty).Trim().ToLowerInvariant();
        if (period is "cairo_month" or "monthly")
        {
            var today = MembershipOperational.TodayCairo();
            var start = new DateOnly(today.Year, today.Month, 1);
            return (CairoMidnightUtc(start), CairoMidnightUtc(start.AddMonths(1)));
        }

        // "membership", "one_time", and unknown/legacy values are scoped to this membership.
        return (CairoMidnightUtc(membership.StartDate), CairoMidnightUtc(membership.EndDate.AddDays(1)));
    }

    private static DateTime CairoMidnightUtc(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(
            local, TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"));
    }

    /// <summary>Remaining credits for a limited-mode entitlement; null when not limited.</summary>
    public async Task<int?> RemainingQuotaAsync(
        Guid tenantId, Guid memberId, Guid activityId, Membership coveringMembership, CancellationToken ct = default)
    {
        var ent = await _db.PlanEntitlements.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.PlanId == coveringMembership.PlanId
                                      && e.ActivityId == activityId && !e.IsDeleted, ct);
        if (ent == null || ent.AccessMode != EligibilityLimited || ent.QuotaLimit is null)
            return null;
        var consumed = await CountConsumedForPeriodAsync(
            tenantId, memberId, activityId, coveringMembership, ent.QuotaPeriod, ct);
        return Math.Max(0, ent.QuotaLimit.Value - consumed);
    }

    /// <summary>
    /// All plan entitlements for a membership (desk Member 360 / current membership).
    /// Limited rows include used + remaining; unlimited/included have null remaining.
    /// </summary>
    public async Task<List<MemberActivityQuotaDto>> ListQuotasForMembershipAsync(
        Guid tenantId, Guid memberId, Membership membership, CancellationToken ct = default)
    {
        var ents = await _db.PlanEntitlements.AsNoTracking()
            .Include(e => e.Activity)
            .Where(e => e.TenantId == tenantId
                        && e.PlanId == membership.PlanId
                        && !e.IsDeleted)
            .OrderBy(e => e.Activity != null ? e.Activity.Name : "")
            .ToListAsync(ct);

        var list = new List<MemberActivityQuotaDto>();
        foreach (var e in ents)
        {
            if (e.Activity == null || e.Activity.IsDeleted)
                continue;

            int? used = null;
            int? remaining = null;
            if (e.AccessMode == EligibilityLimited && e.QuotaLimit.HasValue)
            {
                used = await CountConsumedForPeriodAsync(
                    tenantId, memberId, e.ActivityId, membership, e.QuotaPeriod, ct);
                remaining = Math.Max(0, e.QuotaLimit.Value - used.Value);
            }

            list.Add(new MemberActivityQuotaDto
            {
                ActivityId = e.ActivityId,
                ActivityName = e.Activity.Name,
                ActivityNameAr = e.Activity.NameAr,
                ActivityKind = e.Activity.Kind,
                AccessMode = e.AccessMode,
                QuotaLimit = e.QuotaLimit,
                QuotaRemaining = remaining,
                QuotaUsed = used,
                QuotaPeriod = e.QuotaPeriod
            });
        }

        return list;
    }
}

public interface IActivityEntitlementService
{
    Task<ActivityEntitlement?> ResolveAsync(Guid tenantId, Guid memberId, Guid activityId, CancellationToken ct = default);
    Task<int> CountConsumedAsync(Guid tenantId, Guid memberId, Guid activityId, Membership coveringMembership, CancellationToken ct = default);
    Task<int?> RemainingQuotaAsync(Guid tenantId, Guid memberId, Guid activityId, Membership coveringMembership, CancellationToken ct = default);
    Task<List<MemberActivityQuotaDto>> ListQuotasForMembershipAsync(
        Guid tenantId, Guid memberId, Membership membership, CancellationToken ct = default);
}

/// <summary>Resolved entitlement for a member + activity.</summary>
public record ActivityEntitlement(Membership CoveringMembership, PlanEntitlement? Entitlement)
{
    /// <summary>'included' | 'limited' | 'unlimited' | 'not_entitled'</summary>
    public string Mode => Entitlement?.AccessMode switch
    {
        "included" => ActivityEntitlementService.EligibilityIncluded,
        "unlimited" => ActivityEntitlementService.EligibilityUnlimited,
        "limited" => ActivityEntitlementService.EligibilityLimited,
        _ => ActivityEntitlementService.EligibilityNotEntitled
    };

    public bool IsEntitled => Mode != ActivityEntitlementService.EligibilityNotEntitled;

    public int? QuotaLimit => Entitlement?.QuotaLimit;
}
