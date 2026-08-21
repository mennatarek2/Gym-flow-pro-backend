namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Members;
using GMS.Application.Utilities;

/// <summary>
/// Validation rules for UpdateMemberRequest.
/// All fields optional — validated only when provided.
/// </summary>
public class UpdateMemberValidator : AbstractValidator<UpdateMemberRequest>
{
    public UpdateMemberValidator()
    {
        RuleFor(x => x.FullName)
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.FullName));

        RuleFor(x => x.FullNameAr)
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.FullNameAr));

        RuleFor(x => x.Phone)
            .Must(phone => PhoneNormalizer.Normalize(phone) != null)
            .When(x => !string.IsNullOrEmpty(x.Phone))
            .WithMessage("Phone must be a valid Egyptian mobile (+20… or 01x…) / الرقم لازم يكون موبايل مصري صحيح");

        RuleFor(x => x.NationalId)
            .Length(14).When(x => !string.IsNullOrEmpty(x.NationalId))
            .WithMessage("National ID must be exactly 14 digits");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
    }
}
