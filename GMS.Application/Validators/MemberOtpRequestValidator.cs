namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Auth;

public class MemberOtpRequestValidator : AbstractValidator<MemberOtpRequest>
{
    public MemberOtpRequestValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required.")
            .Matches(@"^\+?[1-9]\d{6,14}$").WithMessage("Phone number must be in international format (e.g. +201234567890).");

        RuleFor(x => x.GymCode)
            .NotEmpty().WithMessage("Gym code is required.");
    }
}
