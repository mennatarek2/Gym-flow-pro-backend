namespace GMS.Application.DTOs.Invoices;

/// <summary>Optional filters + pagination for GET /api/invoices. All filter fields are optional.</summary>
public class InvoiceQueryRequest
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? MemberId { get; set; }

    /// <summary>'issued' | 'voided'</summary>
    public string? Status { get; set; }

    /// <summary>'invoice' | 'credit_note'</summary>
    public string? Type { get; set; }

    /// <summary>Optional SaleLine.LineType filter: membership | retail | drop_in | trial | day_pass | fee.</summary>
    public string? LineType { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
