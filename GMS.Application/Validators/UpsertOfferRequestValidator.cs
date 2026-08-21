namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Offers;
using GMS.Core.Constants;

public class UpsertOfferRequestValidator : AbstractValidator<UpsertOfferRequest>
{
    public UpsertOfferRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Offer name is required / اسم العرض مطلوب")
            .MaximumLength(150);

        RuleFor(x => x.ShortDescription).MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(2000);

        RuleFor(x => x.End)
            .GreaterThanOrEqualTo(x => x.Start)
            .WithMessage("End date must be on or after start / يجب أن يكون تاريخ الانتهاء بعد أو يساوي تاريخ البدء");

        RuleFor(x => x.AppliesTo)
            .Must(v => OfferAppliesTo.All.Contains(NormalizeApplies(v)))
            .WithMessage("AppliesTo must be memberships, products, or both");

        RuleFor(x => x.DiscountType)
            .Must(v => OfferDiscountTypes.All.Contains(NormalizeDiscount(v)))
            .WithMessage("DiscountType must be percentage, fixed, or bxgy");

        RuleFor(x => x.Redemption)
            .Must(v => OfferRedemptions.All.Contains(NormalizeRedemption(v)))
            .WithMessage("Redemption must be automatic or promoCode");

        RuleFor(x => x.Value)
            .InclusiveBetween(1, 100)
            .When(x => NormalizeDiscount(x.DiscountType) == OfferDiscountTypes.Percentage && x.Value.HasValue)
            .WithMessage("Percent value must be between 1 and 100");

        RuleFor(x => x.Value)
            .GreaterThan(0)
            .When(x => NormalizeDiscount(x.DiscountType) == OfferDiscountTypes.Fixed && x.Value.HasValue)
            .WithMessage("Fixed value must be greater than 0");

        RuleFor(x => x.BuyQty)
            .GreaterThan(0)
            .When(x => NormalizeDiscount(x.DiscountType) == OfferDiscountTypes.Bxgy && x.BuyQty.HasValue);

        RuleFor(x => x.GetQty)
            .GreaterThan(0)
            .When(x => NormalizeDiscount(x.DiscountType) == OfferDiscountTypes.Bxgy && x.GetQty.HasValue);

        RuleFor(x => x.PromoCode)
            .NotEmpty()
            .When(x => !x.IsDraft
                       && NormalizeRedemption(x.Redemption) == OfferRedemptions.PromoCode
                       && NormalizeDiscount(x.DiscountType) != OfferDiscountTypes.Bxgy)
            .WithMessage("Promo code is required when redemption is Promo Code / كود الخصم مطلوب");

        RuleFor(x => x.DisplayOrder).GreaterThan(0);
    }

    internal static string NormalizeApplies(string? v) =>
        string.IsNullOrWhiteSpace(v) ? OfferAppliesTo.Memberships : v.Trim().ToLowerInvariant();

    internal static string NormalizeDiscount(string? v)
    {
        var s = (v ?? "").Trim().ToLowerInvariant();
        return s == "percent" ? OfferDiscountTypes.Percentage : s;
    }

    internal static string NormalizeRedemption(string? v)
    {
        var s = (v ?? "").Trim();
        if (s.Equals("auto", StringComparison.OrdinalIgnoreCase)) return OfferRedemptions.Automatic;
        if (s.Equals("code", StringComparison.OrdinalIgnoreCase)) return OfferRedemptions.PromoCode;
        if (s.Equals("promocode", StringComparison.OrdinalIgnoreCase)) return OfferRedemptions.PromoCode;
        return s;
    }
}
