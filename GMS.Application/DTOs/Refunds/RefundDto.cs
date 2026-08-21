namespace GMS.Application.DTOs.Refunds;

public class RefundDto
{
    public Guid Id { get; set; }
    public Guid SaleId { get; set; }
    public Guid? PaymentTransactionId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>'cash' | 'gateway' | 'credit'</summary>
    public string Method { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
    public Guid RequestedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>'requested' | 'approved' | 'executed' | 'rejected'</summary>
    public string Status { get; set; } = string.Empty;

    public string? RejectionNote { get; set; }
    public Guid? CreditNoteInvoiceId { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>True when this approve fully restored retail stock (INVS-7 full-sale refund only).</summary>
    public bool StockRestored { get; set; }
}
