namespace GMS.Core.Entities;

/// <summary>
/// An immutable legal invoice or credit note, gap-free sequentially numbered per
/// tenant/year/type via <see cref="InvoiceSequence"/>. Member/line details are snapshotted at
/// issue time so later edits to the member or sale never retroactively change an issued document.
/// </summary>
public class Invoice : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>'invoice' | 'credit_note'</summary>
    public string Type { get; set; } = "invoice";

    /// <summary>Format: INV-YYYY-000001 / CN-YYYY-000001.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    public Guid? SaleId { get; set; }

    public Guid? RefundId { get; set; }

    /// <summary>Self-FK: the invoice a credit note reverses.</summary>
    public Guid? OriginalInvoiceId { get; set; }

    public string MemberNameSnapshot { get; set; } = string.Empty;
    public string MemberPhoneSnapshot { get; set; } = string.Empty;

    /// <summary>JSON snapshot of the sale's line items at issue time.</summary>
    public string LinesSnapshot { get; set; } = string.Empty;

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "EGP";

    public DateTime IssuedAt { get; set; }
    public string? PdfUrl { get; set; }

    /// <summary>'issued' | 'voided'</summary>
    public string Status { get; set; } = "issued";
    public string? VoidReason { get; set; }
    public Guid? VoidedByUserId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Sale? Sale { get; set; }
    public Refund? Refund { get; set; }
    public Invoice? OriginalInvoice { get; set; }
    public AppUser? VoidedByUser { get; set; }
}
