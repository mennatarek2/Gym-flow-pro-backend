namespace GMS.Core.Entities;

/// <summary>
/// A two-step refund request against a <see cref="Sale"/>: requested, then approved (which executes
/// the refund immediately — cash drawer movement, gateway API call, or member credit — in the same
/// transaction), or rejected.
/// </summary>
public class Refund : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid SaleId { get; set; }

    /// <summary>The specific payment being reversed — nullable (e.g. a credit-method refund with no originating gateway payment).</summary>
    public Guid? PaymentTransactionId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>'cash' | 'gateway' | 'credit'</summary>
    public string Method { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public Guid RequestedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>'requested' | 'approved' | 'executed' | 'rejected'</summary>
    public string Status { get; set; } = "requested";

    public string? RejectionNote { get; set; }

    /// <summary>The credit-note Invoice issued for this refund, once created.</summary>
    public Guid? CreditNoteInvoiceId { get; set; }

    public DateTime? ExecutedAt { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Sale? Sale { get; set; }
    public PaymentTransaction? PaymentTransaction { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public AppUser? ApprovedByUser { get; set; }
    public Invoice? CreditNoteInvoice { get; set; }
}
