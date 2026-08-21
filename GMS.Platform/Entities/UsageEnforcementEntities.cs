namespace GMS.Platform.Entities;

/// <summary>
/// Monthly usage snapshot per tenant/metric. Cap is denormalized from tier_feature_map at rollup time.
/// </summary>
public class UsageCounter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    /// <summary>YYYY-MM</summary>
    public string Period { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public int Count { get; set; }
    public int? Cap { get; set; }
    public decimal? OverageBilledEgp { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-tenant temporary grant/deny of a feature, layered on top of tier_feature_map.
/// </summary>
public class FeatureOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string FeatureKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid GrantedByPlatformUserId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Hard-coded source of truth for what each plan tier includes and numeric caps.
/// CapValue null on a cap metric means unlimited; CapValue null/0 on a module key means included as boolean.
/// Module inclusion: CapValue is unused (null); presence of the row means the feature is in the tier.
/// </summary>
public class TierFeatureMap
{
    public string Tier { get; set; } = string.Empty;
    public string FeatureKey { get; set; } = string.Empty;
    public int? CapValue { get; set; }
}
