namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.Common;
using GMS.Application.DTOs.Auth;

public class MemberActivateRequestValidator : AbstractValidator<MemberActivateRequest>
{
    public MemberActivateRequestValidator()
    {
        RuleFor(x => x.GymCode)
            .NotEmpty().WithMessage(AppMessageCatalog.Get("GYM_CODE_REQUIRED").Slash)
            .MaximumLength(50);

        RuleFor(x => x.ActivationCode)
            .NotEmpty().WithMessage(AppMessageCatalog.Get("ACTIVATION_CODE_REQUIRED").Slash)
            .MaximumLength(32);
    }
}
