namespace GMS.Application.DTOs.Invoices;

public class InvoiceDto
{
    public Guid Id { get; set; }

    /// <summary>'invoice' | 'credit_note'</summary>
    public string Type { get; set; } = string.Empty;

    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid? SaleId { get; set; }
    public Guid? OriginalInvoiceId { get; set; }

    public string MemberNameSnapshot { get; set; } = string.Empty;
    public string MemberPhoneSnapshot { get; set; } = string.Empty;

    public List<InvoiceLineSnapshotDto> Lines { get; set; } = new();

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "EGP";

    public DateTime IssuedAt { get; set; }
    public string? PdfUrl { get; set; }

    /// <summary>'issued' | 'voided'</summary>
    public string Status { get; set; } = string.Empty;
    public string? VoidReason { get; set; }
}
