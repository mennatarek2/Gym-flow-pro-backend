namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Plans;

/// <summary>
/// Validation rules for CreatePlanRequest.
/// Kept in sync with CreatePlanRequestValidator (all PlanTypes + session/time rules).
/// </summary>
public class CreatePlanValidator : AbstractValidator<CreatePlanRequest>
{
    private static readonly string[] ValidPlanTypes =
    {
        "monthly_unlimited", "session_pack", "time_limited", "pt_credits", "family", "trial", "day_pass"
    };

    public CreatePlanValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Plan name is required / اسم الخطة مطلوب")
            .MaximumLength(200).WithMessage("Plan name cannot exceed 200 characters");

        RuleFor(x => x.NameAr)
            .NotEmpty().WithMessage("Arabic plan name is required / اسم الخطة بالعربي مطلوب")
            .MaximumLength(200);

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0).WithMessage("Price cannot be negative / لا يمكن أن يكون السعر سالبًا");

        RuleFor(x => x.Price)
            .Equal(0m)
            .When(x => x.PlanType?.ToLowerInvariant() == "trial")
            .WithMessage("Trial plans must have a price of 0 / يجب أن يكون سعر الخطة التجريبية صفرًا");

        RuleFor(x => x.DurationDays)
            .GreaterThan(0).WithMessage("Duration must be greater than 0 days / المدة يجب أن تكون أكبر من 0");

        RuleFor(x => x.PlanType)
            .NotEmpty()
            .Must(x => ValidPlanTypes.Contains(x.ToLower()))
            .WithMessage("Invalid plan type / نوع الخطة غير صحيح");

        // When plan type is session_pack, SessionCount must be 10, 20, or 50
        RuleFor(x => x.SessionCount)
            .Must(x => x == 10 || x == 20 || x == 50)
            .When(x => x.PlanType?.ToLower() == "session_pack")
            .WithMessage("Session count must be 10, 20, or 50 / عدد الجلسات يجب أن يكون 10 أو 20 أو 50");

        // When plan type is time_limited, both TimeRestrictionStart and End must be provided
        RuleFor(x => x.TimeRestrictionStart)
            .NotNull()
            .When(x => x.PlanType?.ToLower() == "time_limited")
            .WithMessage("Time restriction start is required for time-limited plans / وقت البداية مطلوب للخطط المحدودة بالوقت");

        RuleFor(x => x.TimeRestrictionEnd)
            .NotNull()
            .When(x => x.PlanType?.ToLower() == "time_limited")
            .WithMessage("Time restriction end is required for time-limited plans / وقت النهاية مطلوب للخطط المحدودة بالوقت");

        // Validate that end time is after start time
        RuleFor(x => x.TimeRestrictionEnd)
            .GreaterThan(x => x.TimeRestrictionStart ?? TimeOnly.MinValue)
            .When(x => x.TimeRestrictionStart.HasValue && x.TimeRestrictionEnd.HasValue)
            .WithMessage("End time must be after start time / وقت النهاية يجب أن يكون بعد وقت البداية");

        RuleFor(x => x.InvitationQuota)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Invitation quota cannot be negative / حصة الدعوات لا يمكن أن تكون سالبة");

        RuleFor(x => x.ReferralInviteQuota)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Invitation quota cannot be negative / حصة الدعوات لا يمكن أن تكون سالبة");
    }
}
