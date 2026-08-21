namespace GMS.Application.DTOs.Sales;

/// <summary>Request body for POST /api/sales.</summary>
public class CreateSaleRequest
{
    /// <summary>Optional if provided via the X-Idempotency-Key header instead.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// For membership/trial lines: exactly one of MemberId / NewMember.
    /// For retail-only: both may be omitted (walk-in).
    /// </summary>
    public Guid? MemberId { get; set; }
    public NewMemberRequest? NewMember { get; set; }

    /// <summary>Legacy membership-plan sale. Ignored when <see cref="Lines"/> is non-empty.</summary>
    public Guid? PlanId { get; set; }

    /// <summary>Optional warehouse for retail stock deduction (defaults to tenant default warehouse).</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>
    /// Explicit cart lines (membership/retail/…). When null/empty, falls back to legacy
    /// <see cref="PlanId"/>-only membership sale.
    /// </summary>
    public List<CreateSaleLineRequest>? Lines { get; set; }

    public string? PromoCode { get; set; }
    public ManualDiscountRequest? ManualDiscount { get; set; }

    public List<SalePaymentRequest> Payments { get; set; } = new();
    public PartialPaymentRequest? PartialPayment { get; set; }

    /// <summary>Optional referral attribution for this sale's member (INV-3).</summary>
    public string? ReferralCode { get; set; }

    /// <summary>Optional referring GymMember.Id.</summary>
    public Guid? ReferringMemberId { get; set; }
}

/// <summary>One cart line for generalized POS (INVS-6).</summary>
public class CreateSaleLineRequest
{
    /// <summary>membership | trial | day_pass | retail | fee</summary>
    public string LineType { get; set; } = string.Empty;

    public Guid? ProductId { get; set; }
    public Guid? PlanId { get; set; }

    public int Qty { get; set; } = 1;

    /// <summary>Override unit price; defaults to product SellPrice or plan Price.</summary>
    public decimal? UnitPrice { get; set; }
}
