namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Members;
using GMS.Application.Utilities;

/// <summary>
/// Validation rules for CreateMemberRequest.
/// </summary>
public class CreateMemberValidator : AbstractValidator<CreateMemberRequest>
{
    public CreateMemberValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required / الاسم بالكامل مطلوب")
            .MaximumLength(200).WithMessage("Full name cannot exceed 200 characters");

        RuleFor(x => x.FullNameAr)
            .NotEmpty().WithMessage("Arabic name is required / الاسم بالعربي مطلوب")
            .MaximumLength(200);

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone number is required / رقم الهاتف مطلوب")
            .Must(phone => PhoneNormalizer.Normalize(phone) != null)
            .WithMessage("Phone must be a valid Egyptian mobile (+20… or 01x…) / الرقم لازم يكون موبايل مصري صحيح");

        RuleFor(x => x.DateOfBirth)
            .NotEmpty().WithMessage("Date of birth is required / تاريخ الميلاد مطلوب")
            .LessThan(DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)))
            .WithMessage("Member must be at least 10 years old / العضو لازم يكون عمره 10 سنين على الأقل");

        RuleFor(x => x.NationalId)
            .Length(14).When(x => !string.IsNullOrEmpty(x.NationalId))
            .WithMessage("National ID must be exactly 14 digits / الرقم القومي لازم يكون 14 رقم");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage("Invalid email format / صيغة البريد الإلكتروني غير صحيحة");
    }
}
