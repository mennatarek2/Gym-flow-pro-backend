namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Plans;

public class CreatePlanRequestValidator : AbstractValidator<CreatePlanRequest>
{
    private static readonly string[] ValidPlanTypes =
    {
        "monthly_unlimited", "session_pack", "time_limited", "pt_credits", "family", "trial", "day_pass"
    };

    public CreatePlanRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required / الاسم مطلوب");
        RuleFor(x => x.NameAr).NotEmpty().WithMessage("Arabic name is required / الاسم بالعربية مطلوب");

        RuleFor(x => x.PlanType)
            .Must(t => ValidPlanTypes.Contains(t.ToLowerInvariant()))
            .WithMessage("Invalid plan type / نوع الخطة غير صالح");

        RuleFor(x => x.DurationDays)
            .GreaterThan(0).WithMessage("Duration must be positive / يجب أن تكون المدة أكبر من صفر");

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0).WithMessage("Price cannot be negative / لا يمكن أن يكون السعر سالبًا");

        // Trial plans are free by definition — enforced here (not at the DB level) since it's a
        // business rule about how staff configure plans, not a structural data constraint.
        RuleFor(x => x.Price)
            .Equal(0m)
            .When(x => x.PlanType.ToLowerInvariant() == "trial")
            .WithMessage("Trial plans must have a price of 0 / يجب أن يكون سعر الخطة التجريبية صفرًا");

        RuleFor(x => x.ReferralRewardType)
            .Must(t => string.IsNullOrWhiteSpace(t)
                       || t!.Trim().Equals("credit", StringComparison.OrdinalIgnoreCase)
                       || t.Trim().Equals("free_days", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Referral reward type must be credit or free_days / نوع مكافأة الإحالة غير صالح");

        RuleFor(x => x.ReferralRewardValue)
            .GreaterThan(0)
            .When(x => !string.IsNullOrWhiteSpace(x.ReferralRewardType))
            .WithMessage("Referral reward value must be positive when type is set / قيمة المكافأة يجب أن تكون موجبة");

        RuleFor(x => x.ReferralInviteQuota)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Invitation quota cannot be negative / حصة الدعوات لا يمكن أن تكون سالبة");

        // PRIVATE (pt_credits): included PT session count is the core of the package.
        RuleFor(x => x.SessionCount)
            .NotNull().GreaterThan(0)
            .When(x => x.PlanType.ToLowerInvariant() == "pt_credits")
            .WithMessage("Included sessions must be a positive number for PRIVATE plans / عدد الجلسات المضمنة يجب أن يكون رقمًا موجبًا لخطط البرايفت");

        RuleFor(x => x.PtSessionDurationMinutes)
            .Must(v => v is 30 or 45 or 60 or 90)
            .When(x => x.PlanType.ToLowerInvariant() == "pt_credits" && x.PtSessionDurationMinutes.HasValue)
            .WithMessage("Session duration must be 30, 45, 60, or 90 minutes / مدة الجلسة يجب أن تكون 30 أو 45 أو 60 أو 90 دقيقة");
    }
}
