namespace GMS.Infrastructure.Configuration;

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using GMS.Core.Configuration;

public class LocalOwnerRecoveryFileContent
{
    public Guid RequestId { get; set; }
    public string InstallationId { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string? GymName { get; set; }
    public string Nonce { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string Method { get; set; } = "online";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public Guid? ApprovalJti { get; set; }
    public string? ApprovalToken { get; set; }
    public DateTime? PasswordResetAtUtc { get; set; }
    public bool PlatformCompletionRecorded { get; set; }
    public bool OnlineSubmitted { get; set; }
    public string ChallengeText { get; set; } = string.Empty;
}

/// <summary>
/// DPAPI-protected owner-recovery state for this gym PC. Separate entropy from the license and
/// device-session files. Never stores a password.
/// </summary>
public class LocalOwnerRecoveryStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HyMotion.LocalOwnerRecoveryStore.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _path;
    private readonly ILogger<LocalOwnerRecoveryStore> _logger;

    public LocalOwnerRecoveryStore(ILogger<LocalOwnerRecoveryStore> logger, string? path = null)
    {
        _logger = logger;
        _path = path ?? LocalRuntimePaths.OwnerRecoveryFile;
    }

    public LocalOwnerRecoveryFileContent? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var bytes = File.ReadAllBytes(_path);
            var json = OperatingSystem.IsWindows() ? UnprotectWindows(bytes) : Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<LocalOwnerRecoveryFileContent>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the local owner recovery store; treating as none.");
            return null;
        }
    }

    public void Save(LocalOwnerRecoveryFileContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        content.ApprovalToken = string.IsNullOrWhiteSpace(content.ApprovalToken) ? null : content.ApprovalToken;
        if (string.Equals(content.Status, "completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(content.Status, "rejected", StringComparison.OrdinalIgnoreCase)
            || string.Equals(content.Status, "expired", StringComparison.OrdinalIgnoreCase)
            || string.Equals(content.Status, "revoked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(content.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            content.ApprovalToken = null;
        }

        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(content, JsonOptions);
        var bytes = OperatingSystem.IsWindows() ? ProtectWindows(json) : Encoding.UTF8.GetBytes(json);
        var tempPath = _path + ".tmp";
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, _path, overwrite: true);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clear the local owner recovery store.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(string json) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.LocalMachine);

    [SupportedOSPlatform("windows")]
    private static string UnprotectWindows(byte[] bytes) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.LocalMachine));
}
