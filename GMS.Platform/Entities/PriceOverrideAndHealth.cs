namespace GMS.Platform.Entities;

/// <summary>
/// Time-boxed GymFlow list-price discount for a tenant. Consumed by renewal invoice generation.
/// </summary>
public class PriceOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    /// <summary>percent | fixed</summary>
    public string DiscountType { get; set; } = "percent";
    public decimal Value { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid GrantedByPlatformUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Rules-based tenant churn early-warning score (CP7). One row per tenant; recomputed nightly.
/// Explicitly NOT an ML model — Phase 6 deferral.
/// </summary>
public class TenantHealthScore
{
    public Guid TenantId { get; set; }
    public int Score { get; set; }
    /// <summary>healthy | watch | at_risk | critical</summary>
    public string RiskBand { get; set; } = "healthy";
    /// <summary>Detailed per-signal JSON for support reps (contributing_factors).</summary>
    public string? ContributingFactorsJson { get; set; }
    public DateTime ComputedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Optional assignee on the Platform Console risk queue.</summary>
    public Guid? AssignedPlatformUserId { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
}

/// <summary>
/// Append-only risk-queue outcome log — mirrors tenant call_outcomes shape
/// (actor + outcome + note against a subject), one layer up for platform support.
/// </summary>
public class RiskQueueOutcome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    /// <summary>platform_admin_users.Id who logged the outcome.</summary>
    public Guid PlatformUserId { get; set; }
    /// <summary>contacted | retained | churned | no_answer | watching</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
