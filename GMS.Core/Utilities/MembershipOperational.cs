namespace GMS.Core.Utilities;

using GMS.Core.Entities;

/// <summary>
/// Shared Cairo-day membership rules for list, current, check-in search, and expiry jobs.
/// Effective status is date-aware; stored Status alone is not enough for front-desk decisions.
/// </summary>
public static class MembershipOperational
{
    private static readonly TimeZoneInfo CairoTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    public static DateOnly TodayCairo() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));

    public static DateOnly ToCairoDate(DateTime utc)
    {
        var instant = utc.Kind == DateTimeKind.Utc
            ? utc
            : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(instant, CairoTimeZone));
    }

    /// <summary>
    /// Inclusive Cairo calendar range as a half-open UTC interval [start, end).
    /// Same conversion Z-Report uses for a single business day.
    /// </summary>
    public static (DateTime UtcStart, DateTime UtcEndExclusive) CairoInclusiveRangeUtc(
        DateOnly from, DateOnly toInclusive)
    {
        var cairoStart = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var cairoEnd = DateTime.SpecifyKind(
            toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(cairoStart, CairoTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(cairoEnd, CairoTimeZone));
    }

    /// <summary>
    /// Date-aware status for APIs/UI.
    /// "scheduled" = stored active but StartDate is still in the future.
    /// </summary>
    public static string GetEffectiveStatus(Membership membership, DateOnly? today = null)
    {
        today ??= TodayCairo();
        var status = (membership.Status ?? string.Empty).Trim().ToLowerInvariant();

        if (status is "cancelled" or "pending")
            return status;

        if (status == "expired")
            return "expired";

        if (status == "frozen")
            return membership.EndDate < today.Value ? "expired" : "frozen";

        if (status == "active")
        {
            if (membership.EndDate < today.Value)
                return "expired";
            if (membership.StartDate > today.Value)
                return "scheduled";
            return "active";
        }

        return string.IsNullOrEmpty(status) ? "none" : status;
    }

    public static bool IsCheckinEligible(Membership membership, DateOnly? today = null)
    {
        today ??= TodayCairo();
        return GetEffectiveStatus(membership, today) == "active"
            && membership.StartDate <= today.Value
            && membership.EndDate >= today.Value;
    }

    /// <summary>
    /// Membership that covers Cairo today: stored active/frozen and in the date window.
    /// Cancelled, pending, expired, and scheduled rows are never covering.
    /// Invitation quota and desk renew dating must use this — not SelectOperational.
    /// </summary>
    public static bool IsCoveringToday(Membership? membership, DateOnly? today = null)
    {
        today ??= TodayCairo();
        return membership != null
            && (membership.Status == "active" || membership.Status == "frozen")
            && membership.StartDate <= today.Value
            && membership.EndDate >= today.Value;
    }

    /// <summary>
    /// Current covering membership (latest EndDate, then latest StartDate).
    /// Null when the member has no in-window active/frozen plan — including after a full refund.
    /// </summary>
    public static Membership? SelectCoveringToday(
        IEnumerable<Membership>? memberships,
        DateOnly? today = null)
    {
        today ??= TodayCairo();
        return (memberships ?? Enumerable.Empty<Membership>())
            .Where(m => m != null && IsCoveringToday(m, today))
            .OrderByDescending(m => m.EndDate)
            .ThenByDescending(m => m.StartDate)
            .FirstOrDefault();
    }

    /// <summary>
    /// Single selection rule for operations screens:
    /// 1) covering today (active/frozen in window — latest EndDate)
    /// 2) upcoming active (soonest StartDate)
    /// 3) pending (newest)
    /// 4) past-end stale active / expired (latest EndDate)
    /// 5) any remaining including cancelled (latest EndDate) — display only
    /// </summary>
    public static Membership? SelectOperational(
        IEnumerable<Membership>? memberships,
        DateOnly? today = null)
    {
        today ??= TodayCairo();
        var list = memberships?.Where(m => m != null).ToList() ?? new List<Membership>();
        if (list.Count == 0)
            return null;

        var live = SelectCoveringToday(list, today);
        if (live != null)
            return live;

        var upcoming = list
            .Where(m => m.Status == "active" && m.StartDate > today.Value)
            .OrderBy(m => m.StartDate)
            .ThenByDescending(m => m.EndDate)
            .FirstOrDefault();
        if (upcoming != null)
            return upcoming;

        var pending = list
            .Where(m => m.Status == "pending")
            .OrderByDescending(m => m.CreatedAtUtc)
            .FirstOrDefault();
        if (pending != null)
            return pending;

        var pastOrExpired = list
            .Where(m =>
                m.Status == "expired"
                || ((m.Status == "active" || m.Status == "frozen") && m.EndDate < today.Value))
            .OrderByDescending(m => m.EndDate)
            .ThenByDescending(m => m.StartDate)
            .FirstOrDefault();
        if (pastOrExpired != null)
            return pastOrExpired;

        return list
            .OrderByDescending(m => m.EndDate)
            .ThenByDescending(m => m.StartDate)
            .FirstOrDefault();
    }

    /// <summary>
    /// Persist expiry when EndDate has passed (active/frozen → expired).
    /// </summary>
    public static bool TryMarkExpired(Membership membership, DateOnly today)
    {
        if ((membership.Status == "active" || membership.Status == "frozen")
            && membership.EndDate < today)
        {
            membership.Status = "expired";
            membership.UpdatedAtUtc = DateTime.UtcNow;
            return true;
        }

        return false;
    }
}
