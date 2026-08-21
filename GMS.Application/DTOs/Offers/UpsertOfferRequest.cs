namespace GMS.Application.DTOs.Offers;

public class UpsertOfferRequest
{
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
    public List<Guid>? PlanIds { get; set; }
    public List<Guid>? ProductIds { get; set; }
    public List<string>? MembershipLabels { get; set; }
    public List<string>? ProductLabels { get; set; }

    /// <summary>percentage | fixed | bxgy (percent accepted as percentage)</summary>
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

    public bool ShowOnMemberApp { get; set; }
    public bool Featured { get; set; }
    public bool ShowBanner { get; set; }
    public int DisplayOrder { get; set; } = 1;

    /// <summary>automatic | promoCode (auto | code accepted)</summary>
    public string Redemption { get; set; } = "automatic";
    public string? PromoCode { get; set; }

    public bool IsDraft { get; set; }
}
