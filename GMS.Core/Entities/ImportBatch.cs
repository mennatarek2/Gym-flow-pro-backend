namespace GMS.Core.Entities;

/// <summary>
/// A single Excel/CSV bulk-import run: uploaded file, column mapping, and row-level progress
/// counters for the members+memberships import pipeline.
/// </summary>
public class ImportBatch : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid UploadedByUserId { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string FileBlobUrl { get; set; } = string.Empty;

    /// <summary>'members_memberships' — the only scope supported so far.</summary>
    public string EntityScope { get; set; } = "members_memberships";

    /// <summary>'validating' | 'dry_run_ready' | 'importing' | 'completed' | 'rolled_back' | 'failed'</summary>
    public string Status { get; set; } = "validating";

    public int TotalRows { get; set; }
    public int OkRows { get; set; }
    public int ErrorRows { get; set; }

    /// <summary>JSON: {"sourceHeader": "targetField", ...} — auto-detected via fuzzy header
    /// matching at upload time, overridable via SetMappingAsync.</summary>
    public string? MappingJson { get; set; }

    public DateTime? CompletedAt { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public AppUser? UploadedByUser { get; set; }
    public ICollection<ImportRow> Rows { get; set; } = new List<ImportRow>();
}
