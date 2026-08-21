namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Shifts;

public class OpenShiftRequestValidator : AbstractValidator<OpenShiftRequest>
{
    public OpenShiftRequestValidator()
    {
        RuleFor(x => x.OpeningFloat)
            .GreaterThanOrEqualTo(0).WithMessage("Opening float cannot be negative / لا يمكن أن يكون الرصيد الافتتاحي سالبًا");
    }
}
