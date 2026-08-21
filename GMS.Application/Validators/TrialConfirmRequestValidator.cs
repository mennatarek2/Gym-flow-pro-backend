namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Trials;

public class TrialConfirmRequestValidator : AbstractValidator<TrialConfirmRequest>
{
    public TrialConfirmRequestValidator()
    {
        RuleFor(x => x.PhoneNumber).NotEmpty().WithMessage("Phone number is required / رقم الهاتف مطلوب");

        RuleFor(x => x.Otp)
            .NotEmpty().WithMessage("OTP is required / رمز التحقق مطلوب")
            .Length(6).WithMessage("OTP must be 6 digits / يجب أن يتكون رمز التحقق من 6 أرقام");
    }
}
