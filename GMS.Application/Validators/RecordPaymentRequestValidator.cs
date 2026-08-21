namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Sales;

public class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.Method)
            .NotEmpty().WithMessage("Payment method is required / طريقة الدفع مطلوبة")
            .Must(m => SalePaymentRequestValidator.ValidMethods.Contains(m))
            .WithMessage("Invalid payment method / طريقة دفع غير صالحة");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Payment amount must be greater than 0 / يجب أن يكون مبلغ الدفع أكبر من 0");
    }
}
