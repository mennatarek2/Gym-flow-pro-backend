namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Attendance;

public class QrCheckinRequestValidator : AbstractValidator<QrCheckinRequest>
{
    public QrCheckinRequestValidator()
    {
        RuleFor(x => x.GymCode)
            .NotEmpty().WithMessage("Gym code is required / رمز الصالة مطلوب")
            .MaximumLength(500).WithMessage("Invalid QR format / صيغة رمز QR غير صالحة");
    }
}
