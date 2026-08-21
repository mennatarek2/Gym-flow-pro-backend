namespace GMS.Core.Models;

/// <summary>
/// Everything IInvoicePdfRenderer needs to render one invoice/credit-note PDF — a flat snapshot,
/// not an EF entity, so the renderer has no persistence dependency.
/// </summary>
public class InvoicePdfModel
{
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>'invoice' | 'credit_note'</summary>
    public string Type { get; set; } = "invoice";

    public DateTime IssuedAt { get; set; }

    public string TenantName { get; set; } = string.Empty;
    public string TenantNameAr { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? LogoUrl { get; set; }
    /// <summary>Embedded logo bytes for PDF. Null when missing or unreadable — never required.</summary>
    public byte[]? LogoImageBytes { get; set; }
    /// <summary>data: URI for HTML print (srcdoc). Prefer this over LogoUrl inside iframes.</summary>
    public string? LogoDataUri { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Website { get; set; }
    /// <summary>Gym Identity primary hex, default GymFlowPro lime.</summary>
    public string PrimaryColor { get; set; } = "#7ACC00";
    public string AccentColor { get; set; } = "#A0E040";

    public string MemberName { get; set; } = string.Empty;
    public string MemberPhone { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public decimal? PaymentAmount { get; set; }
    public DateTime? PaidAt { get; set; }
    /// <summary>'issued' | 'voided' — presentation only.</summary>
    public string Status { get; set; } = "issued";

    public List<InvoicePdfLineModel> Lines { get; set; } = new();

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "EGP";

    public string? FooterText { get; set; }
    public string? FooterTextAr { get; set; }

    /// <summary>
    /// Optional neutral labels so the same renderer can serve tenant invoices and GymFlow platform
    /// invoices without forking templates.
    /// </summary>
    public string BillerCodeLabel { get; set; } = "Code";
    public string CustomerLabel { get; set; } = "Customer";
    public string CustomerLabelAr { get; set; } = "العميل";
}

public class InvoicePdfLineModel
{
    public string Description { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public int Qty { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
