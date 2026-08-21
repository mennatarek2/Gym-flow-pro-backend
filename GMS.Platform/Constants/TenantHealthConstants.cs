namespace GMS.Platform.Constants;

/// <summary>Risk bands for platform.tenant_health_scores (rules-based churn early-warning).</summary>
public static class TenantRiskBands
{
    public const string Healthy = "healthy";
    public const string Watch = "watch";
    public const string AtRisk = "at_risk";
    public const string Critical = "critical";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Healthy, Watch, AtRisk, Critical
    };

    public static readonly HashSet<string> QueueDefault = new(StringComparer.OrdinalIgnoreCase)
    {
        AtRisk, Critical
    };

    public static bool IsValid(string? band) =>
        !string.IsNullOrWhiteSpace(band) && All.Contains(band.Trim());
}

/// <summary>Signal keys written into contributing_factors JSON and PlatformHealth:Weights.</summary>
public static class TenantHealthSignals
{
    public const string LoginFrequency = "login_frequency";
    public const string FeatureBreadth = "feature_breadth";
    public const string PaymentHealth = "payment_health";
    public const string MemberBaseTrend = "member_base_trend";
    public const string SupportTicketVolume = "support_ticket_volume";
    public const string UsageVsCap = "usage_vs_cap";

    public static readonly string[] All =
    {
        LoginFrequency,
        FeatureBreadth,
        PaymentHealth,
        MemberBaseTrend,
        SupportTicketVolume,
        UsageVsCap
    };
}

/// <summary>Risk-queue outcome values — call-sheet shape, platform-side vocabulary.</summary>
public static class RiskQueueOutcomes
{
    public const string Contacted = "contacted";
    public const string Retained = "retained";
    public const string Churned = "churned";
    public const string NoAnswer = "no_answer";
    public const string Watching = "watching";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Contacted, Retained, Churned, NoAnswer, Watching
    };

    public static bool IsValid(string? outcome) =>
        !string.IsNullOrWhiteSpace(outcome) && All.Contains(outcome.Trim());
}
