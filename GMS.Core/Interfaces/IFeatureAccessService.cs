namespace GMS.Core.Interfaces;

/// <summary>
/// Single evaluator for module feature access (tier map → overrides → Phase A JSON deny overlay).
/// Used by HTTP FeatureFlagFilter and Hangfire job guards — no parallel gate.
/// </summary>
public interface IFeatureAccessService
{
    Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default);

    /// <summary>Drop Redis cache entries for a tenant (subscription / override / settings changes).</summary>
    Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Result of comparing live usage against the tenant's plan cap for a metric.</summary>
public sealed class CapCheckResult
{
    public bool Allowed { get; init; }
    public bool SoftWarning { get; init; }
    public int Count { get; init; }
    public int? Cap { get; init; }
    public string Metric { get; init; } = string.Empty;
}

/// <summary>
/// Plan usage / seat cap checks. staff_seats is a hard block; active_members is soft warning only;
/// whatsapp_messages is soft overage (billed at rollup); branches is deferred (count only).
/// </summary>
public interface ITierEnforcementService
{
    Task<CapCheckResult> CheckCapAsync(Guid tenantId, string metric, CancellationToken cancellationToken = default);
}
