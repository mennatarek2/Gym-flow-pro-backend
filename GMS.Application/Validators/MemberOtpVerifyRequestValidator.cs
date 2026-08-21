namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Auth;

public class MemberOtpVerifyRequestValidator : AbstractValidator<MemberOtpVerifyRequest>
{
    public MemberOtpVerifyRequestValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required.")
            .Matches(@"^\+?[1-9]\d{6,14}$").WithMessage("Phone number must be in international format.");

        RuleFor(x => x.GymCode)
            .NotEmpty().WithMessage("Gym code is required.");

        RuleFor(x => x.Otp)
            .NotEmpty().WithMessage("OTP code is required.")
            .Length(6).WithMessage("OTP must be exactly 6 digits.")
            .Matches(@"^\d{6}$").WithMessage("OTP must contain only digits.");
    }
}
