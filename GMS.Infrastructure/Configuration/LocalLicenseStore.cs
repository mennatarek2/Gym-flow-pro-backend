namespace GMS.Infrastructure.Configuration;

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GMS.Core.Licensing;
using Microsoft.Extensions.Logging;

internal class LocalLicenseFileContent
{
    public string InstallationId { get; set; } = string.Empty;
    public string? MachineFingerprint { get; set; }
    public LocalLicensePayload? License { get; set; }
}

/// <summary>
/// Persists this installation's identity and most-recently-verified license payload to
/// %ProgramData%\HyMotion\secrets\local-license.dat, DPAPI-protected (LocalMachine scope) -
/// identical convention to LocalSecretProvider (same write-temp-then-move safety, same rationale
/// for why DPAPI over "just hide a JSON file": rule 20 explicitly rejects license.json-by-
/// obscurity). A different Entropy string than LocalSecretProvider's so the two blobs can't be
/// cross-decrypted into each other.
/// </summary>
public class LocalLicenseStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HyMotion.LocalLicenseStore.v1");

    private readonly string _path;
    private readonly ILogger<LocalLicenseStore> _logger;

    public LocalLicenseStore(ILogger<LocalLicenseStore> logger, string? path = null)
    {
        _logger = logger;
        _path = path ?? GMS.Core.Configuration.LocalRuntimePaths.LicenseFile;
    }

    /// <summary>Persisted installation id when the license store already exists. Does not mint a
    /// new id — session restore must not create an identity as a side effect.</summary>
    public string? TryGetInstallationId()
    {
        var id = Load()?.InstallationId;
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    /// <summary>Returns the persisted installation id, generating and persisting a new one (a
    /// random GUID, not a hardware serial - rule 14/29) the first time this is called on a fresh
    /// install. Stable across restarts and reinstalls that preserve %ProgramData%.</summary>
    public string GetOrCreateInstallationId()
    {
        var content = Load() ?? new LocalLicenseFileContent();
        if (!string.IsNullOrWhiteSpace(content.InstallationId))
            return content.InstallationId;

        content.InstallationId = GenerateInstallationId();
        content.MachineFingerprint ??= ComputeMachineFingerprint();
        Save(content);
        return content.InstallationId;
    }

    public string? GetMachineFingerprint() => Load()?.MachineFingerprint;

    public LocalLicensePayload? GetLicense() => Load()?.License;

    public void SaveLicense(LocalLicensePayload payload)
    {
        var content = Load() ?? new LocalLicenseFileContent { InstallationId = GetOrCreateInstallationId() };
        content.License = payload;
        Save(content);
    }

    private static string GenerateInstallationId()
    {
        // "INS-XXXXXXXXXX" - random, not derived from anything guessable/enumerable.
        var bytes = RandomNumberGenerator.GetBytes(5);
        return $"INS-{Convert.ToHexString(bytes)}";
    }

    /// <summary>One coarse, already-present-on-every-Windows-box identifier (the OS's own
    /// per-install MachineGuid), hashed - not raw, not a full hardware inventory. This is a
    /// secondary signal only (see LocalInstallation.MachineFingerprint remarks); the real anti-
    /// resale enforcement is the server-side device-limit check, not this value.</summary>
    private static string? ComputeMachineFingerprint()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var machineGuid = key?.GetValue("MachineGuid") as string;
            if (string.IsNullOrWhiteSpace(machineGuid)) return null;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineGuid));
            return Convert.ToHexString(hash);
        }
        catch
        {
            return null; // never let fingerprinting failure block activation
        }
    }

    private LocalLicenseFileContent? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var bytes = File.ReadAllBytes(_path);
            var json = OperatingSystem.IsWindows() ? UnprotectWindows(bytes) : Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<LocalLicenseFileContent>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the local license store at {Path}; treating as no license present.", _path);
            return null;
        }
    }

    private void Save(LocalLicenseFileContent content)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(content);
        var bytes = OperatingSystem.IsWindows() ? ProtectWindows(json) : Encoding.UTF8.GetBytes(json);

        var tempPath = _path + ".tmp";
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, _path, overwrite: true);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(string json) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.LocalMachine);

    [SupportedOSPlatform("windows")]
    private static string UnprotectWindows(byte[] bytes) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.LocalMachine));
}
