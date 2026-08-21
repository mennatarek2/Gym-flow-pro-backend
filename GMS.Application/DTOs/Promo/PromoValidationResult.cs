namespace GMS.Application.DTOs.Promo;

/// <summary>
/// Outcome of IPromoService.ValidateAndPriceAsync. When <see cref="IsValid"/> is false,
/// <see cref="FailureReason"/> holds one of the constants in GMS.Core.Constants.PromoValidationReasons
/// and the pricing fields are unset.
/// </summary>
public class PromoValidationResult
{
    public bool IsValid { get; set; }
    public string? FailureReason { get; set; }

    public Guid? PromoCodeId { get; set; }
    public string? Code { get; set; }

    /// <summary>'percent' | 'fixed'</summary>
    public string? Type { get; set; }

    public decimal? OriginalPrice { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? FinalPrice { get; set; }
}
