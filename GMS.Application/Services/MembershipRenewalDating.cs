using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;

namespace GMS.Application.Services;

/// <summary>
/// Pure date math for desk renew / plan-change transition modes.
/// Uses Cairo calendar day when <paramref name="today"/> is omitted.
/// </summary>
public static class MembershipRenewalDating
{
    public static bool IsCoveringToday(Membership? membership, DateOnly today)
        => MembershipOperational.IsCoveringToday(membership, today);

    /// <summary>
    /// Compute new membership start/end for a renew.
    /// Unknown modes must be rejected by the caller before invoking.
    /// Day-pass always maps to a single Cairo day (today).
    /// When nothing covers today, mode is ignored and the period restarts from today.
    /// </summary>
    public static (DateOnly Start, DateOnly End) Calculate(
        Membership? coveringOrLatest,
        MembershipPlan plan,
        string transitionMode,
        DateOnly? today = null)
    {
        today ??= MembershipOperational.TodayCairo();

        if (plan.PlanType == "day_pass")
            return (today.Value, today.Value);

        var covering = IsCoveringToday(coveringOrLatest, today.Value);
        if (!covering)
            return (today.Value, today.Value.AddDays(plan.DurationDays));

        var mode = transitionMode.Trim().ToLowerInvariant();
        return mode switch
        {
            PlanTransitionModes.QueueNext => QueueNext(coveringOrLatest!, plan),
            PlanTransitionModes.ManualRollover => (
                today.Value,
                coveringOrLatest!.EndDate.AddDays(plan.DurationDays)),
            // cancel_and_switch (default)
            _ => (today.Value, today.Value.AddDays(plan.DurationDays))
        };
    }

    private static (DateOnly Start, DateOnly End) QueueNext(Membership covering, MembershipPlan plan)
    {
        var start = covering.EndDate.AddDays(1);
        return (start, start.AddDays(plan.DurationDays));
    }

    /// <summary>
    /// Expire open priors according to transition mode. Skips the newly created membership.
    /// Option A clips EndDate to today; Option B leaves priors untouched; Option C expires without clip.
    /// </summary>
    public static void ApplyPriorOpenHandling(
        IEnumerable<Membership> openMemberships,
        Guid newMembershipId,
        string transitionMode,
        DateOnly today,
        bool apply)
    {
        if (!apply)
            return;

        var mode = transitionMode.Trim().ToLowerInvariant();
        if (mode == PlanTransitionModes.QueueNext)
            return;

        foreach (var prior in openMemberships)
        {
            if (prior.Id == newMembershipId)
                continue;
            if (prior.Status != "active" && prior.Status != "frozen")
                continue;

            prior.Status = "expired";
            prior.UpdatedAtUtc = DateTime.UtcNow;

            if (mode == PlanTransitionModes.CancelAndSwitch && prior.EndDate > today)
                prior.EndDate = today;
        }
    }
}
