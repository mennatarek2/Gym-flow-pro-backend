namespace GMS.Application.Interfaces;

using GMS.Application.DTOs.Backup;

/// <summary>
/// Read-only view over the Backup &amp; Recovery subsystem for the Local Edition admin page.
/// Deliberately does not perform backup/restore itself - see BackupHealthService remarks for why.
/// </summary>
public interface IBackupHealthService
{
    Task<BackupHealthDto> GetHealthAsync();
    Task<List<BackupHistoryItemDto>> GetHistoryAsync(int take = 30);

    /// <summary>Starts scripts/local-install/backup/Backup-HyMotion.ps1 -Type Manual as a detached
    /// process and returns immediately - does not wait for it to finish (a full backup can take
    /// minutes). The caller should poll GetHealthAsync afterward to see the new entry appear.</summary>
    Task<TriggerBackupResponse> TriggerManualBackupAsync();
}
