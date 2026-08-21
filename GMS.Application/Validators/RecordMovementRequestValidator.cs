namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Shifts;

public class RecordMovementRequestValidator : AbstractValidator<RecordMovementRequest>
{
    private static readonly string[] ValidTypes = { "paid_in", "paid_out", "float_adjust" };

    public RecordMovementRequestValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Movement type is required / نوع الحركة مطلوب")
            .Must(t => ValidTypes.Contains(t))
            .WithMessage("Type must be one of paid_in, paid_out, float_adjust / يجب أن يكون النوع أحد: paid_in، paid_out، float_adjust");

        RuleFor(x => x.Amount)
            .NotEqual(0).WithMessage("Amount cannot be zero / لا يمكن أن يكون المبلغ صفرًا");

        RuleFor(x => x.Reason)
            .MaximumLength(200).WithMessage("Reason cannot exceed 200 characters / السبب لا يمكن أن يتجاوز 200 حرف");
    }
}
