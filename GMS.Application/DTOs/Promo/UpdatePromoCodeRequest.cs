namespace GMS.Application.DTOs.Promo;

public class UpdatePromoCodeRequest
{
    /// <summary>Stored uppercased regardless of input case.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>'percent' | 'fixed'</summary>
    public string Type { get; set; } = "percent";
    public decimal Value { get; set; }

    /// <summary>Plan ids this code applies to, or null/empty = all plans.</summary>
    public List<Guid>? AppliesTo { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }

    public int? MaxUses { get; set; }
    public int? MaxUsesPerMember { get; set; }
    public decimal? MinPrice { get; set; }
}
