namespace GMS.Core.Entities;

/// <summary>
/// A discount code redeemable against one or more membership plans.
/// </summary>
public class PromoCode : BaseEntity
{
    public Guid TenantId { get; set; }

    public string Code { get; set; } = string.Empty;

    /// <summary>'percent' | 'fixed'</summary>
    public string Type { get; set; } = "percent";

    public decimal Value { get; set; }

    /// <summary>JSON array of plan ids this code applies to, or null = all plans.</summary>
    public string? AppliesTo { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }

    public int? MaxUses { get; set; }
    public int? MaxUsesPerMember { get; set; }
    public int UsesCount { get; set; } = 0;

    public decimal? MinPrice { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Tenant? Tenant { get; set; }
}
