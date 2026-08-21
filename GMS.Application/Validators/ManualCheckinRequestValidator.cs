namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Attendance;
using GMS.Core.Enums;

public class ManualCheckinRequestValidator : AbstractValidator<ManualCheckinRequest>
{
    public ManualCheckinRequestValidator()
    {
        RuleFor(x => x.MemberId)
            .NotEmpty().WithMessage("Member ID is required / معرف العضو مطلوب");

        RuleFor(x => x.Reason)
            .IsInEnum().WithMessage("Invalid check-in reason / سبب تسجيل الدخول غير صالح");

        RuleFor(x => x.Notes)
            .MaximumLength(500).WithMessage("Notes must not exceed 500 characters / الملاحظات يجب ألا تتجاوز 500 حرف")
            .NotEmpty().When(x => x.Reason == ManualCheckinReason.Other)
            .WithMessage("Notes are required when reason is 'Other' / الملاحظات مطلوبة عند اختيار 'أخرى'");
    }
}
