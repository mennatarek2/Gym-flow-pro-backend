namespace GMS.Infrastructure.Configuration;

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

public sealed class LocalDeviceSessionFileContent
{
    public string RefreshToken { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public DateTime SavedAtUtc { get; set; }
}

/// <summary>
/// Keep-signed-in credential for HyMotion Local Edition. DPAPI LocalMachine at
/// %ProgramData%\HyMotion\secrets\device-session.dat — same protection as LocalLicenseStore,
/// different entropy so the blobs cannot be cross-decrypted. Gym identity lives in SQL / the
/// license store; this file is auth-only and is safe to delete on logout.
/// Never stores a password.
/// </summary>
public class LocalDeviceSessionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HyMotion.LocalDeviceSessionStore.v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly ILogger<LocalDeviceSessionStore> _logger;

    public LocalDeviceSessionStore(ILogger<LocalDeviceSessionStore> logger, string? path = null)
    {
        _logger = logger;
        _path = path ?? GMS.Core.Configuration.LocalRuntimePaths.DeviceSessionFile;
    }

    public void Save(LocalDeviceSessionFileContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(content.RefreshToken))
            throw new ArgumentException("Refresh token is required.", nameof(content));
        if (string.IsNullOrWhiteSpace(content.GymCode))
            throw new ArgumentException("Gym code is required.", nameof(content));
        if (string.IsNullOrWhiteSpace(content.InstallationId))
            throw new ArgumentException("Installation id is required.", nameof(content));

        content.GymCode = content.GymCode.Trim();
        content.InstallationId = content.InstallationId.Trim();
        content.RefreshToken = content.RefreshToken.Trim();
        if (content.SavedAtUtc == default)
            content.SavedAtUtc = DateTime.UtcNow;

        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(content, JsonOptions);
        var bytes = OperatingSystem.IsWindows() ? ProtectWindows(json) : Encoding.UTF8.GetBytes(json);

        var tempPath = _path + ".tmp";
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, _path, overwrite: true);
        _logger.LogInformation("Local device session saved for gym {GymCode}.", content.GymCode);
    }

    public LocalDeviceSessionFileContent? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var bytes = File.ReadAllBytes(_path);
            var json = OperatingSystem.IsWindows() ? UnprotectWindows(bytes) : Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<LocalDeviceSessionFileContent>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the local device session at {Path}; treating as signed out.", _path);
            return null;
        }
    }

    /// <summary>
    /// Returns the stored session only when it still belongs to this gym and this installation.
    /// Mismatch (New Gym, cloned PC with a copied file that still decrypts, stale debug API)
    /// clears the file so a refresh token cannot restore the wrong gym.
    /// </summary>
    public LocalDeviceSessionFileContent? LoadIfBound(string? gymCode, string? installationId)
    {
        var session = Load();
        if (session == null) return null;

        if (string.IsNullOrWhiteSpace(gymCode)
            || string.IsNullOrWhiteSpace(installationId)
            || string.IsNullOrWhiteSpace(session.RefreshToken)
            || !GymCodesEqual(session.GymCode, gymCode)
            || !string.Equals(session.InstallationId, installationId, StringComparison.Ordinal))
        {
            Clear();
            return null;
        }

        return session;
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
            var tmp = _path + ".tmp";
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clear the local device session at {Path}.", _path);
        }
    }

    public static bool GymCodesEqual(string? a, string? b) =>
        string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(string json) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.LocalMachine);

    [SupportedOSPlatform("windows")]
    private static string UnprotectWindows(byte[] bytes) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.LocalMachine));
}
