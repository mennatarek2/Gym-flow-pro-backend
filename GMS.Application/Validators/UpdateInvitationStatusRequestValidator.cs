namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Invitation;
using GMS.Core.Constants;

public class UpdateInvitationStatusRequestValidator : AbstractValidator<UpdateInvitationStatusRequest>
{
    public UpdateInvitationStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(InvitationStatuses.IsValid)
            .WithMessage("Status must be New, Contacted, Interested, Not Interested, or Converted / حالة غير صالحة");
    }
}
