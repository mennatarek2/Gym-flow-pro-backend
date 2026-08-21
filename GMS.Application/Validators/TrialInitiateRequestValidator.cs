namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Trials;

public class TrialInitiateRequestValidator : AbstractValidator<TrialInitiateRequest>
{
    public TrialInitiateRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Full name is required / الاسم الكامل مطلوب");
        RuleFor(x => x.PhoneNumber).NotEmpty().WithMessage("Phone number is required / رقم الهاتف مطلوب");
        RuleFor(x => x.PlanId).NotEmpty().WithMessage("Plan is required / الخطة مطلوبة");
    }
}
