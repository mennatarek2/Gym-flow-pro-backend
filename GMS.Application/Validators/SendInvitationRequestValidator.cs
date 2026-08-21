namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Invitation;
using GMS.Application.Utilities;

public class SendInvitationRequestValidator : AbstractValidator<SendInvitationRequest>
{
    public SendInvitationRequestValidator()
    {
        RuleFor(x => x.ResolvedName)
            .NotEmpty().WithMessage("Name is required / الاسم مطلوب")
            .MaximumLength(200).WithMessage("Name too long / الاسم طويل جداً");

        RuleFor(x => x.ResolvedPhone)
            .NotEmpty().WithMessage("Phone number is required / رقم الهاتف مطلوب")
            .Must(phone => PhoneNormalizer.Normalize(phone) != null)
            .WithMessage("Invalid Egyptian mobile number / رقم الموبايل غير صالح");

        RuleFor(x => x.NationalId)
            .Length(14)
            .When(x => !string.IsNullOrWhiteSpace(x.NationalId))
            .WithMessage("National ID must be 14 digits / الرقم القومي يجب أن يكون 14 رقم");

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => x.Notes != null);
    }
}
