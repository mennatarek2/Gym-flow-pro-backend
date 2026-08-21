namespace GMS.Application.DTOs.Offers;

/// <summary>Member-visible offer. Never includes the raw promo code string.</summary>
public class MemberOfferDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string ShortDescription { get; set; } = string.Empty;
    public string? ShortDescriptionAr { get; set; }
    public string? Description { get; set; }
    public string? BannerUrl { get; set; }

    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }

    /// <summary>memberships | products | both</summary>
    public string AppliesTo { get; set; } = "memberships";
    public List<string> MembershipLabels { get; set; } = new();
    public List<string> ProductLabels { get; set; } = new();

    /// <summary>percentage | fixed | bxgy</summary>
    public string DiscountType { get; set; } = "percentage";
    public string DiscountLabel { get; set; } = string.Empty;
    public int? BuyQty { get; set; }
    public int? GetQty { get; set; }

    public bool NewMembersOnly { get; set; }
    public bool ShowOnMemberApp { get; set; }
    public bool Featured { get; set; }
    public bool ShowBanner { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>automatic | promoCode</summary>
    public string Redemption { get; set; } = "automatic";

    /// <summary>Set when redemption is promoCode — never the actual code.</summary>
    public string? PromoCodeHint { get; set; }

    /// <summary>active (list only returns active)</summary>
    public string Status { get; set; } = "active";
}
