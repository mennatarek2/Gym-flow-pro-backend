namespace GMS.Core.Entities;

/// <summary>
/// An immutable daily closing snapshot for a tenant's Cairo business day, capturing payment-method
/// breakdown, sales/discounts, shift reconciliation rows, and outstanding balances. Once created for
/// a given (TenantId, ReportDate), <see cref="Services.ZReportService.BuildAsync"/> returns the
/// existing snapshot rather than recomputing — the only way to recompute is
/// <see cref="Services.ZReportService.RegenerateAsync"/> (manager+, audited).
/// </summary>
public class ZReport : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>The Cairo (Egypt Standard Time) business day this snapshot covers.</summary>
    public DateOnly ReportDate { get; set; }

    /// <summary>JSON snapshot of the computed aggregation (see <see cref="Models.ZReportPdfModel"/> for the rendered shape).</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public string? PdfUrl { get; set; }

    public DateTime GeneratedAt { get; set; }

    /// <summary>app_users.Id of the staff member who triggered generation; null for the automated nightly job.</summary>
    public Guid? GeneratedByUserId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public AppUser? GeneratedByUser { get; set; }
}
