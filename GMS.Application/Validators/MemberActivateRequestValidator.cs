namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Auth;

public class MemberActivateRequestValidator : AbstractValidator<MemberActivateRequest>
{
    public MemberActivateRequestValidator()
    {
        RuleFor(x => x.GymCode)
            .NotEmpty().WithMessage("Gym code is required.")
            .MaximumLength(50);

        RuleFor(x => x.ActivationCode)
            .NotEmpty().WithMessage("Activation code is required.")
            .MaximumLength(32);
    }
}
