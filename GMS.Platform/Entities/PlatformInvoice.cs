namespace GMS.Platform.Entities;

/// <summary>
/// GymFlow-issued invoice billed to a tenant customer. Numbering is global per year (not per
/// tenant) via <see cref="PlatformInvoiceSequence" />.
/// </summary>
public class PlatformInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SubscriptionId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty; // GFP-2026-000123
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = "issued"; // issued | paid | overdue | voided
    public DateOnly DueDate { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }
    public string? PaymentLink { get; set; }
    public string? EtaUuid { get; set; }
    public string? PdfUrl { get; set; }
    /// <summary>Immutable JSON snapshot of invoice lines (subscription + overage), same pattern as tenant invoices.</summary>
    public string? LinesSnapshot { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Gap-free invoice sequence for GymFlow's own billing plane. One row per year.
/// </summary>
public class PlatformInvoiceSequence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Year { get; set; }
    public int LastNumber { get; set; }
}

/// <summary>
/// Stores processed platform payment gateway events so duplicate webhook deliveries are a no-op.
/// </summary>
public class PlatformPaymentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid InvoiceId { get; set; }
    public string Gateway { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ExternalRef { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool HmacVerified { get; set; }
    public string? RawPayload { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
