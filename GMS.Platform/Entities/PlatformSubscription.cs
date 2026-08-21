namespace GMS.Platform.Entities;

/// <summary>
/// Source of truth for what a tenant pays for and account state.
/// At most one live row per tenant (trialing|active|past_due) — enforced by filtered unique index.
/// </summary>
public class PlatformSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>starter | growth | pro | enterprise</summary>
    public string PlanTier { get; set; } = "growth";

    /// <summary>trialing | active | past_due | suspended | cancelled</summary>
    public string Status { get; set; } = "trialing";

    /// <summary>monthly | annual</summary>
    public string BillingCycle { get; set; } = "monthly";

    public decimal PriceEgp { get; set; }
    public DateOnly CurrentPeriodStart { get; set; }
    public DateOnly CurrentPeriodEnd { get; set; }
    public DateTime? TrialEndsAtUtc { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    /// <summary>Set when status becomes suspended (CP5); used for check-in grace buffer.</summary>
    public DateTime? SuspendedAtUtc { get; set; }
    public bool AutoRenewOptIn { get; set; }
    public string? SavedCardToken { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<SubscriptionChange> Changes { get; set; } = new List<SubscriptionChange>();
}

/// <summary>
/// Append-only history of subscription mutations. Every write to subscriptions must pair with a row here.
/// </summary>
public class SubscriptionChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SubscriptionId { get; set; }

    /// <summary>upgrade | downgrade | cycle_change | reactivation | cancellation | trial_start</summary>
    public string ChangeType { get; set; } = string.Empty;

    public string? FromTier { get; set; }
    public string? ToTier { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public decimal? ProratedAmountEgp { get; set; }

    /// <summary>self_serve | platform_admin | system</summary>
    public string InitiatedBy { get; set; } = "system";

    public Guid? PlatformAdminUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public PlatformSubscription? Subscription { get; set; }
}
