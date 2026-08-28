namespace GMS.Platform.Entities;

/// <summary>
/// Platform commercial plan catalog — one row per fixed tier (starter|growth|pro|enterprise).
/// List prices here; subscriptions freeze PriceEgp at creation / explicit tier change.
/// </summary>
public class CommercialPlan
{
    public string Tier { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActiveForSales { get; set; } = true;
    public bool IsDefault { get; set; }
    public decimal MonthlyPriceEgp { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Field-level commercial plan change history — complements platform_audit_log.
/// </summary>
public class PlanChangeLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tier { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public Guid ActorPlatformUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
