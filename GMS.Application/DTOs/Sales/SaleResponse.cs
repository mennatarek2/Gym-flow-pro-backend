namespace GMS.Application.DTOs.Sales;

public class SaleResponse
{
    public Guid SaleId { get; set; }
    public Guid? MembershipId { get; set; }
    public bool IsReplay { get; set; }

    /// <summary>"ready" | "queued" | "skipped" (total == 0) | "not_applicable" (payment-only response)</summary>
    public string InvoiceStatus { get; set; } = "queued";

    /// <summary>Set when the invoice was created inline before the sale response returned.</summary>
    public Guid? InvoiceId { get; set; }

    /// <summary>Snapshotted invoice number when <see cref="InvoiceId"/> is set (e.g. INV-2026-00042).</summary>
    public string? InvoiceNumber { get; set; }

    public SaleTotalsDto Totals { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    /// <summary>Receipt HTML path when available: /api/invoices/{id}/receipt-html (?paymentId= for debt payments).</summary>
    public string? ReceiptUrl { get; set; }
}
