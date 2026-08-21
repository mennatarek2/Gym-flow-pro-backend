namespace GMS.Application.DTOs.Imports;

public class ImportBatchDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;

    /// <summary>'validating' | 'dry_run_ready' | 'importing' | 'completed' | 'rolled_back' | 'failed'</summary>
    public string Status { get; set; } = string.Empty;

    public int TotalRows { get; set; }
    public int OkRows { get; set; }
    public int ErrorRows { get; set; }

    /// <summary>Auto-detected (or manually corrected) source-header → target-field mapping.</summary>
    public Dictionary<string, string> Mapping { get; set; } = new();

    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
