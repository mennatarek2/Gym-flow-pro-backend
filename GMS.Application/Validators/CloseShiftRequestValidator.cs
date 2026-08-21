namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Shifts;

public class CloseShiftRequestValidator : AbstractValidator<CloseShiftRequest>
{
    public CloseShiftRequestValidator()
    {
        RuleFor(x => x.CountedCash)
            .GreaterThanOrEqualTo(0).WithMessage("Counted cash cannot be negative / لا يمكن أن يكون المبلغ المعدود سالبًا");

        RuleFor(x => x.VarianceNote)
            .MaximumLength(300).WithMessage("Variance note cannot exceed 300 characters / ملاحظة الفرق لا يمكن أن تتجاوز 300 حرف");
    }
}
