namespace GMS.Application.DTOs.Backup;

/// <summary>
/// Backup Health summary for the Settings -&gt; Backup &amp; Recovery admin page (OwnerOnly, Local
/// Edition only). Read-only projection of manifest.json files under
/// GMS.Core.Configuration.LocalRuntimePaths.BackupsDir - the API never performs backup/restore
/// itself (see BackupHealthService remarks); those stay in scripts/local-install/backup/*.ps1 so
/// the Scheduled Task and any manual trigger exercise the exact same, already-reviewed code path.
/// </summary>
public class BackupHealthDto
{
    /// <summary>Overall status: Healthy | Warning | Critical.
    /// Critical means no verified backup exists at all.</summary>
    public string OverallStatus { get; set; } = "Critical";

    public DateTime? LastBackupAtUtc { get; set; }
    public string? LastBackupStatus { get; set; }
    public DateTime? LastVerifiedAtUtc { get; set; }

    public int HealthyBackupCount { get; set; }
    public int RetentionCount { get; set; }

    public string BackupLocation { get; set; } = string.Empty;

    public bool ScheduledTaskFound { get; set; }
    public DateTime? NextScheduledRunAtUtc { get; set; }

    public bool OffPcConfigured { get; set; }
    public string? OffPcDestination { get; set; }
    public bool OffPcLastCopyVerified { get; set; }

    public List<string> RecentFailures { get; set; } = new();
}

public class BackupHistoryItemDto
{
    public string BackupId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string BackupType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool DatabaseVerified { get; set; }
    public bool UploadsVerified { get; set; }
    public long DatabaseSizeBytes { get; set; }
    public long UploadsSizeBytes { get; set; }
    public int UploadFileCount { get; set; }
    public bool OffPcVerified { get; set; }
}

public class TriggerBackupResponse
{
    public bool Started { get; set; }
    public string Message { get; set; } = string.Empty;
}
