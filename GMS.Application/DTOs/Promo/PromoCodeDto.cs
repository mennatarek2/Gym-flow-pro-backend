namespace GMS.Application.DTOs.Promo;

public class PromoCodeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;

    /// <summary>'percent' | 'fixed'</summary>
    public string Type { get; set; } = string.Empty;
    public decimal Value { get; set; }

    /// <summary>Plan ids this code applies to, or null = all plans.</summary>
    public List<Guid>? AppliesTo { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }

    public int? MaxUses { get; set; }
    public int? MaxUsesPerMember { get; set; }
    public int UsesCount { get; set; }

    public decimal? MinPrice { get; set; }
    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
