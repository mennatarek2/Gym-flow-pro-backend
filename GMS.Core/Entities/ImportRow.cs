namespace GMS.Core.Entities;

/// <summary>
/// One source-file row of an <see cref="ImportBatch"/>: the mapped field values (as JSON),
/// validation outcome, and — once executed — the created member/membership it produced.
/// </summary>
public class ImportRow : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid BatchId { get; set; }

    public int RowNumber { get; set; }

    /// <summary>JSON of the mapped field values for this row, e.g.
    /// {"fullName":"...","phoneNumber":"+20...","planName":"...", ...}.</summary>
    public string RawJson { get; set; } = string.Empty;

    /// <summary>'ok' | 'error' | 'imported' | 'skipped'</summary>
    public string Status { get; set; } = "ok";

    /// <summary>Comma-separated machine-readable codes, e.g. "PHONE_INVALID,DATE_RANGE_INVALID".</summary>
    public string? ErrorCodes { get; set; }

    public Guid? CreatedMemberId { get; set; }
    public Guid? CreatedMembershipId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public ImportBatch? Batch { get; set; }
}
