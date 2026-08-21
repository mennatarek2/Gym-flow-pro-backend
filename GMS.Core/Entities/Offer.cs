namespace GMS.Core.Entities;

/// <summary>
/// Gym offer. Promo codes are an optional redemption method on the offer — not a separate product.
/// </summary>
public class Offer : BaseEntity
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }

    public string ShortDescription { get; set; } = string.Empty;
    public string? ShortDescriptionAr { get; set; }
    public string? Description { get; set; }
    public string? BannerUrl { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>memberships | products | both</summary>
    public string AppliesTo { get; set; } = "memberships";

    /// <summary>JSON array of plan GUIDs. Empty/null = all in applies-to scope.</summary>
    public string? PlanIdsJson { get; set; }

    /// <summary>JSON array of product GUIDs. Empty/null = all in applies-to scope.</summary>
    public string? ProductIdsJson { get; set; }

    /// <summary>Optional display labels when IDs are empty (e.g. ["3 Months"]).</summary>
    public string? MembershipLabelsJson { get; set; }

    public string? ProductLabelsJson { get; set; }

    /// <summary>percentage | fixed | bxgy</summary>
    public string DiscountType { get; set; } = "percentage";

    public decimal? Value { get; set; }
    public decimal? MaxDiscount { get; set; }
    public int? BuyQty { get; set; }
    public int? GetQty { get; set; }

    public bool AllMembers { get; set; } = true;
    public bool NewMembersOnly { get; set; }

    public decimal? MinPurchase { get; set; }
    public int? UsageLimit { get; set; }
    public int? PerMemberLimit { get; set; }
    public int UsesCount { get; set; }

    public bool ShowOnMemberApp { get; set; }
    public bool Featured { get; set; }
    public bool ShowBanner { get; set; }
    public int DisplayOrder { get; set; } = 1;

    /// <summary>automatic | promoCode</summary>
    public string Redemption { get; set; } = "automatic";

    public string? PromoCode { get; set; }
    public Guid? PromoCodeId { get; set; }

    public bool IsDraft { get; set; }

    public Tenant? Tenant { get; set; }
    public PromoCode? LinkedPromoCode { get; set; }
}
