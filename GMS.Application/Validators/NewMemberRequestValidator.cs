namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Sales;

public class NewMemberRequestValidator : AbstractValidator<NewMemberRequest>
{
    public NewMemberRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required / الاسم بالكامل مطلوب")
            .MaximumLength(200).WithMessage("Full name cannot exceed 200 characters");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required / رقم الهاتف مطلوب")
            .Matches(@"^\+20\d{10}$").WithMessage("Phone must be in +20XXXXXXXXXX format / الرقم لازم يبدأ بـ +20");
    }
}
