namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Auth;

public class EmployeeActivateRequestValidator : AbstractValidator<EmployeeActivateRequest>
{
    public EmployeeActivateRequestValidator()
    {
        RuleFor(x => x.GymCode)
            .NotEmpty().WithMessage("Gym code is required.")
            .MaximumLength(50);

        RuleFor(x => x.ActivationCode)
            .NotEmpty().WithMessage("Activation code is required.")
            .MaximumLength(32);
    }
}
