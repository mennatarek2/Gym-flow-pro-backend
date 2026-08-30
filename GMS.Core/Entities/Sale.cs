namespace GMS.Core.Entities;

/// <summary>
/// A point-of-sale transaction: one or more <see cref="SaleLine"/> items sold to a member
/// (or walk-in, if <see cref="MemberId"/> is null) by a staff member.
/// </summary>
public class Sale : BaseEntity
{
    public Guid TenantId { get; set; }

    public Guid? MemberId { get; set; }
    /// <summary>Snapshot identity for a paid walk-in sale without a GymMember.</summary>
    public string? GuestName { get; set; }
    public string? GuestPhone { get; set; }
    public Guid SoldByUserId { get; set; }

    public Guid? ShiftId { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }

    public Guid? PromoCodeId { get; set; }
    public decimal? ManualDiscountAmount { get; set; }
    public string? ManualDiscountReason { get; set; }

    public decimal AmountDue { get; set; } = 0;
    public DateOnly? DueDate { get; set; }

    /// <summary>'completed' | 'partially_paid' | 'refunded' | 'partially_refunded'</summary>
    public string Status { get; set; } = "completed";

    public string? IdempotencyKey { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public GymMember? Member { get; set; }
    public AppUser? SoldByUser { get; set; }
    public PromoCode? PromoCode { get; set; }
    public Shift? Shift { get; set; }
    public ICollection<SaleLine> Lines { get; set; } = new List<SaleLine>();
}
