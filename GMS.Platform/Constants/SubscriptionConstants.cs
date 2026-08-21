namespace GMS.Platform.Constants;

public static class PlanTiers
{
    public const string Starter = "starter";
    public const string Growth = "growth";
    public const string Pro = "pro";
    public const string Enterprise = "enterprise";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Starter, Growth, Pro, Enterprise
    };

    /// <summary>Higher = more expensive / more capable. Used for upgrade vs downgrade.</summary>
    public static int Rank(string tier) => tier.Trim().ToLowerInvariant() switch
    {
        Starter => 1,
        Growth => 2,
        Pro => 3,
        Enterprise => 4,
        _ => 0
    };

    public static bool IsValid(string? tier) =>
        !string.IsNullOrWhiteSpace(tier) && All.Contains(tier.Trim());
}

public static class SubscriptionStatuses
{
    public const string Trialing = "trialing";
    public const string Active = "active";
    public const string PastDue = "past_due";
    public const string Suspended = "suspended";
    public const string Cancelled = "cancelled";

    /// <summary>Statuses that count as the single "live" row per tenant (partial unique index).</summary>
    public static readonly HashSet<string> Live = new(StringComparer.OrdinalIgnoreCase)
    {
        Trialing, Active, PastDue
    };

    public static bool IsLive(string? status) =>
        !string.IsNullOrWhiteSpace(status) && Live.Contains(status.Trim());
}

public static class BillingCycles
{
    public const string Monthly = "monthly";
    public const string Annual = "annual";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Monthly, Annual
    };

    public static bool IsValid(string? cycle) =>
        !string.IsNullOrWhiteSpace(cycle) && All.Contains(cycle.Trim());
}

public static class SubscriptionChangeTypes
{
    public const string Upgrade = "upgrade";
    public const string Downgrade = "downgrade";
    public const string CycleChange = "cycle_change";
    public const string Reactivation = "reactivation";
    public const string Cancellation = "cancellation";
    public const string TrialStart = "trial_start";
    public const string TrialExtend = "trial_extend";
    public const string PastDue = "past_due";
    public const string Suspension = "suspension";
}

public static class SubscriptionInitiators
{
    public const string SelfServe = "self_serve";
    public const string PlatformAdmin = "platform_admin";
    public const string System = "system";
}

/// <summary>Default EGP list prices — override via PlatformSubscription:Prices in config later.</summary>
public static class PlatformListPrices
{
    public static decimal MonthlyEgp(string tier) => tier.Trim().ToLowerInvariant() switch
    {
        PlanTiers.Starter => 999m,
        PlanTiers.Growth => 1999m,
        PlanTiers.Pro => 3999m,
        PlanTiers.Enterprise => 7999m,
        _ => 0m
    };

    public static decimal ForCycle(string tier, string cycle) =>
        string.Equals(cycle, BillingCycles.Annual, StringComparison.OrdinalIgnoreCase)
            ? MonthlyEgp(tier) * 10m // ~2 months free on annual
            : MonthlyEgp(tier);
}
