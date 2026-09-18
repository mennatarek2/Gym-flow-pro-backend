namespace GMS.Application.Services;

using System.Diagnostics;
using System.Text.Json;
using GMS.Application.DTOs.Backup;
using GMS.Application.Interfaces;
using GMS.Core.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Read-only Backup Health for the Settings -&gt; Backup &amp; Recovery admin page.
///
/// Deliberately does NOT implement backup or restore itself, even though it lives in the same
/// process that could technically run the SQL commands directly:
///   - The nightly Scheduled Task already runs scripts/local-install/backup/Backup-HyMotion.ps1
///     as NT AUTHORITY\NETWORK SERVICE. If the web API also had a second, separate in-process
///     implementation of "how to take a backup", the two could drift (exactly the SharedScripts/
///     SHARED_SCRIPTS drift bug found earlier this session) and only one of them would ever
///     actually get tested by the real nightly run.
///   - Restore stops/restarts the very Windows Service this API process runs inside of - having
///     the API attempt to orchestrate that from within an HTTP request handling its own service
///     being stopped is a self-termination problem with no clean success path. Restore stays a
///     script an administrator runs directly (Restore-HyMotion.ps1); this page surfaces status
///     and points to it, it does not attempt to trigger it.
/// TriggerManualBackupAsync is the one exception: it just launches the SAME script the Scheduled
/// Task launches, as a detached process, and returns immediately - it does not duplicate any
/// backup logic in C#.
/// </summary>
public class BackupHealthService : IBackupHealthService
{
    private readonly ILogger<BackupHealthService> _logger;

    public BackupHealthService(ILogger<BackupHealthService> logger)
    {
        _logger = logger;
    }

    public Task<BackupHealthDto> GetHealthAsync()
    {
        var manifests = ReadAllManifests();
        var config = ReadBackupConfig();

        var dto = new BackupHealthDto
        {
            BackupLocation = LocalRuntimePaths.BackupsDir,
            RetentionCount = config.retentionCount,
            OffPcConfigured = !string.IsNullOrWhiteSpace(config.offPcDestination),
            OffPcDestination = config.offPcDestination,
        };

        var healthy = manifests.Where(m => m.Status == "Healthy").OrderByDescending(m => m.CreatedAtUtc).ToList();
        var mostRecent = manifests.OrderByDescending(m => m.CreatedAtUtc).FirstOrDefault();

        dto.HealthyBackupCount = healthy.Count;

        if (mostRecent != null)
        {
            dto.LastBackupAtUtc = mostRecent.CreatedAtUtc;
            dto.LastBackupStatus = mostRecent.Status;
        }

        var lastHealthy = healthy.FirstOrDefault();
        if (lastHealthy != null)
        {
            dto.LastVerifiedAtUtc = lastHealthy.DatabaseVerifiedAtUtc;
            dto.OffPcLastCopyVerified = lastHealthy.OffPcVerified;
        }

        dto.RecentFailures = manifests
            .Where(m => m.Status is "Failed" or "Partial")
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(5)
            .Select(m => $"{m.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC - {m.BackupId} - {m.Status}" +
                (m.Errors.Count > 0 ? $" ({m.Errors[0]})" : string.Empty))
            .ToList();

        var taskInfo = QueryScheduledTask();
        dto.ScheduledTaskFound = taskInfo.found;
        dto.NextScheduledRunAtUtc = taskInfo.nextRunUtc;

        dto.OverallStatus = healthy.Count == 0
            ? "Critical"
            : (dto.RecentFailures.Count > 0 || !dto.ScheduledTaskFound ? "Warning" : "Healthy");

        return Task.FromResult(dto);
    }

    public Task<List<BackupHistoryItemDto>> GetHistoryAsync(int take = 30)
    {
        var items = ReadAllManifests()
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(take)
            .Select(m => new BackupHistoryItemDto
            {
                BackupId = m.BackupId,
                CreatedAtUtc = m.CreatedAtUtc,
                BackupType = m.BackupType,
                Status = m.Status,
                DatabaseVerified = m.DatabaseVerified,
                UploadsVerified = m.UploadsVerified,
                DatabaseSizeBytes = m.DatabaseSizeBytes,
                UploadsSizeBytes = m.UploadsSizeBytes,
                UploadFileCount = m.UploadFileCount,
                OffPcVerified = m.OffPcVerified,
            })
            .ToList();
        return Task.FromResult(items);
    }

    public Task<TriggerBackupResponse> TriggerManualBackupAsync()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "install-scripts", "backup", "Backup-HyMotion.ps1");
        if (!File.Exists(scriptPath))
        {
            return Task.FromResult(new TriggerBackupResponse
            {
                Started = false,
                Message = "Backup script not found - this install may predate the backup system or was not upgraded correctly."
            });
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" -Type Manual",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
            _logger.LogInformation("Manual backup triggered via API, script={ScriptPath}", scriptPath);
            return Task.FromResult(new TriggerBackupResponse
            {
                Started = true,
                Message = "Backup started. It runs in the background - refresh Backup Health in a minute to see the result."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start manual backup process");
            return Task.FromResult(new TriggerBackupResponse { Started = false, Message = $"Could not start backup: {ex.Message}" });
        }
    }

    private (bool found, DateTime? nextRunUtc) QueryScheduledTask()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/Query /TN \"HyMotion Nightly Backup\" /FO LIST /V",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return (false, null);
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            if (proc.ExitCode != 0) return (false, null);

            foreach (var line in output.Split('\n'))
            {
                if (line.StartsWith("Next Run Time:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line.Substring("Next Run Time:".Length).Trim();
                    if (DateTime.TryParse(value, out var parsed)) return (true, parsed.ToUniversalTime());
                    return (true, null);
                }
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not query the HyMotion Nightly Backup scheduled task");
            return (false, null);
        }
    }

    private (bool enabled, int retentionCount, string? offPcDestination) ReadBackupConfig()
    {
        try
        {
            if (File.Exists(LocalRuntimePaths.BackupConfigFile))
            {
                var json = File.ReadAllText(LocalRuntimePaths.BackupConfigFile);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var enabled = root.TryGetProperty("enabled", out var e) && e.GetBoolean();
                var retention = root.TryGetProperty("retentionCount", out var r) ? r.GetInt32() : 14;
                var dest = root.TryGetProperty("offPcDestination", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                return (enabled, retention, dest);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read backup-config.json");
        }
        return (true, 14, null);
    }

    private List<ManifestSummary> ReadAllManifests()
    {
        var result = new List<ManifestSummary>();
        if (!Directory.Exists(LocalRuntimePaths.BackupsDir)) return result;

        foreach (var dir in Directory.EnumerateDirectories(LocalRuntimePaths.BackupsDir))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith("HyMotionBackup_", StringComparison.Ordinal)) continue;

            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var summary = new ManifestSummary
                {
                    BackupId = GetString(root, "backupId") ?? name,
                    CreatedAtUtc = GetDateTime(root, "createdAtUtc") ?? DateTime.MinValue,
                    BackupType = GetString(root, "backupType") ?? "Unknown",
                    Status = GetString(root, "status") ?? "Unknown",
                    Errors = GetStringArray(root, "errors"),
                };

                if (root.TryGetProperty("database", out var dbEl) && dbEl.ValueKind == JsonValueKind.Object)
                {
                    summary.DatabaseVerified = GetBool(dbEl, "verified");
                    summary.DatabaseVerifiedAtUtc = GetDateTime(dbEl, "verifiedAtUtc");
                    summary.DatabaseSizeBytes = GetLong(dbEl, "sizeBytes");
                }
                if (root.TryGetProperty("uploads", out var upEl) && upEl.ValueKind == JsonValueKind.Object)
                {
                    summary.UploadsVerified = GetBool(upEl, "verified");
                    summary.UploadsSizeBytes = GetLong(upEl, "sizeBytes");
                    summary.UploadFileCount = (int)GetLong(upEl, "fileCount");
                }
                if (root.TryGetProperty("offPc", out var offEl) && offEl.ValueKind == JsonValueKind.Object)
                {
                    summary.OffPcVerified = GetBool(offEl, "verified");
                }

                result.Add(summary);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse manifest at {Path}", manifestPath);
            }
        }

        return result;
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    private static long GetLong(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.Number) ? v.GetInt64() : 0;

    private static DateTime? GetDateTime(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), out var dt)
            ? dt.ToUniversalTime() : null;

    private static List<string> GetStringArray(JsonElement el, string prop)
    {
        var list = new List<string>();
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var item in v.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString()!);
        return list;
    }

    private class ManifestSummary
    {
        public string BackupId { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public string BackupType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<string> Errors { get; set; } = new();
        public bool DatabaseVerified { get; set; }
        public DateTime? DatabaseVerifiedAtUtc { get; set; }
        public long DatabaseSizeBytes { get; set; }
        public bool UploadsVerified { get; set; }
        public long UploadsSizeBytes { get; set; }
        public int UploadFileCount { get; set; }
        public bool OffPcVerified { get; set; }
    }
}
