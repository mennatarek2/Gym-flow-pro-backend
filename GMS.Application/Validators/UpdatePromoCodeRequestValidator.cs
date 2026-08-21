namespace GMS.Application.Validators;

using System.Text.RegularExpressions;
using FluentValidation;
using GMS.Application.DTOs.Promo;

public class UpdatePromoCodeRequestValidator : AbstractValidator<UpdatePromoCodeRequest>
{
    public UpdatePromoCodeRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Promo code is required / كود الخصم مطلوب")
            .Must(code => Regex.IsMatch(code.ToUpperInvariant(), "^[A-Z0-9_-]{3,30}$"))
            .WithMessage("Promo code must be 3-30 characters: letters, numbers, underscore, or hyphen only / يجب أن يتكون كود الخصم من 3-30 حرفًا (حروف، أرقام، شرطة سفلية أو شرطة)");

        RuleFor(x => x.Type)
            .Must(t => t == "percent" || t == "fixed")
            .WithMessage("Type must be 'percent' or 'fixed' / يجب أن يكون النوع 'percent' أو 'fixed'");

        RuleFor(x => x.Value)
            .InclusiveBetween(1, 100)
            .When(x => x.Type == "percent")
            .WithMessage("Percent value must be between 1 and 100 / يجب أن تكون النسبة بين 1 و 100");

        RuleFor(x => x.Value)
            .GreaterThan(0)
            .When(x => x.Type == "fixed")
            .WithMessage("Fixed value must be greater than 0 / يجب أن تكون القيمة الثابتة أكبر من 0");

        RuleFor(x => x.ValidTo)
            .GreaterThanOrEqualTo(x => x.ValidFrom)
            .WithMessage("ValidTo must be on or after ValidFrom / يجب أن يكون تاريخ الانتهاء بعد أو يساوي تاريخ البدء");

        RuleFor(x => x.MaxUses)
            .GreaterThan(0)
            .When(x => x.MaxUses.HasValue)
            .WithMessage("MaxUses must be greater than 0 / يجب أن يكون الحد الأقصى للاستخدام أكبر من 0");

        RuleFor(x => x.MaxUsesPerMember)
            .GreaterThan(0)
            .When(x => x.MaxUsesPerMember.HasValue)
            .WithMessage("MaxUsesPerMember must be greater than 0 / يجب أن يكون الحد الأقصى للعضو أكبر من 0");
    }
}
