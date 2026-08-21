namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Sales;

public class CreateSaleRequestValidator : AbstractValidator<CreateSaleRequest>
{
    private static readonly HashSet<string> AllowedLineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "membership", "trial", "day_pass", "retail", "fee"
    };

    public CreateSaleRequestValidator()
    {
        RuleFor(x => x.Payments)
            .NotEmpty().WithMessage("At least one payment is required / يجب إدخال دفعة واحدة على الأقل");

        RuleForEach(x => x.Payments).SetValidator(new SalePaymentRequestValidator());

        RuleFor(x => x)
            .Must(HasCart)
            .WithMessage("Provide planId or lines / أدخل planId أو lines");

        RuleFor(x => x)
            .Must(x => !(x.MemberId.HasValue && x.NewMember != null))
            .WithMessage("Provide at most one of memberId or newMember / لا يمكن إرسال memberId و newMember معاً");

        RuleFor(x => x)
            .Must(x => !RequiresMember(x) || x.MemberId.HasValue || x.NewMember != null)
            .WithMessage("Member required for membership/trial lines / العضو مطلوب لأسطر العضوية/التجربة");

        When(x => x.Lines != null && x.Lines.Count > 0, () =>
        {
            RuleForEach(x => x.Lines!).ChildRules(line =>
            {
                line.RuleFor(l => l.LineType)
                    .Must(t => !string.IsNullOrWhiteSpace(t) && AllowedLineTypes.Contains(t.Trim()))
                    .WithMessage("Invalid lineType / نوع السطر غير صالح");

                line.RuleFor(l => l.Qty)
                    .GreaterThan(0).WithMessage("Qty must be positive / الكمية يجب أن تكون موجبة");

                line.When(l => IsRetail(l.LineType), () =>
                {
                    line.RuleFor(l => l.ProductId)
                        .NotEmpty().WithMessage("productId required for retail lines / productId مطلوب لأسطر التجزئة");
                });

                line.When(l => IsMembershipLike(l.LineType), () =>
                {
                    line.RuleFor(l => l.PlanId)
                        .NotEmpty().WithMessage("planId required for membership lines / planId مطلوب لأسطر العضوية");
                });
            });
        });

        When(x => x.NewMember != null, () =>
        {
            RuleFor(x => x.NewMember!).SetValidator(new NewMemberRequestValidator());
        });

        When(x => x.ManualDiscount != null, () =>
        {
            RuleFor(x => x.ManualDiscount!.Amount)
                .GreaterThanOrEqualTo(0).WithMessage("Manual discount amount cannot be negative / لا يمكن أن يكون الخصم اليدوي سالبًا");

            RuleFor(x => x.ManualDiscount!.Reason)
                .NotEmpty()
                .When(x => x.ManualDiscount!.Amount > 0)
                .WithMessage("A reason is required for a manual discount / السبب مطلوب عند تطبيق خصم يدوي");
        });
    }

    private static bool HasCart(CreateSaleRequest x) =>
        (x.Lines != null && x.Lines.Count > 0)
        || (x.PlanId.HasValue && x.PlanId.Value != Guid.Empty);

    private static bool RequiresMember(CreateSaleRequest x)
    {
        if (x.Lines == null || x.Lines.Count == 0)
            return true; // legacy PlanId sale always requires a member

        return x.Lines.Any(l => IsMembershipLike(l.LineType));
    }

    private static bool IsRetail(string? t) =>
        string.Equals(t?.Trim(), "retail", StringComparison.OrdinalIgnoreCase);

    private static bool IsMembershipLike(string? t)
    {
        var v = t?.Trim();
        return string.Equals(v, "membership", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "trial", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "day_pass", StringComparison.OrdinalIgnoreCase);
    }
}
