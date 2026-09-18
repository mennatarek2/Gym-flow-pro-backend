namespace GMS.Infrastructure.Configuration;

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="ILocalSecretProvider"/>. Persists generated secrets as a JSON dictionary in
/// a single file under <paramref name="storeDirectory"/> (defaults to
/// %ProgramData%\HyMotion\secrets — see GMS.Core.Configuration.LocalRuntimePaths). On Windows the
/// file content is protected with DPAPI
/// (CurrentMachine scope) — the safest mechanism available without adding a new dependency on a
/// full secrets-management stack. On non-Windows (dev/test only — Local Edition itself is
/// Windows-first) the file is written unprotected with a warning logged; the future installer
/// should decide whether stronger protection (e.g. a dedicated service account + ACLs) is needed.
/// </summary>
public class LocalSecretProvider : ILocalSecretProvider
{
    private const string FileName = "local-secrets.dat";
    // DPAPI has no "purpose" string; this optional entropy narrows decryption to this exact use
    // so the blob can't be silently reused to decrypt unrelated DPAPI-protected data on the box.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HyMotion.LocalSecretProvider.v1");

    private readonly string _storeDirectory;
    private readonly string _storePath;
    private readonly ILogger<LocalSecretProvider> _logger;

    public LocalSecretProvider(ILogger<LocalSecretProvider> logger, string? storeDirectory = null)
    {
        _logger = logger;
        _storeDirectory = storeDirectory ?? GMS.Core.Configuration.LocalRuntimePaths.SecretsDir;
        _storePath = Path.Combine(_storeDirectory, FileName);
    }

    public IReadOnlyDictionary<string, string> EnsureSecrets(
        IEnumerable<string> requiredKeys, IReadOnlyDictionary<string, string?> existingValues)
    {
        var missing = requiredKeys
            .Where(k => string.IsNullOrWhiteSpace(existingValues.GetValueOrDefault(k)))
            .ToList();

        var result = new Dictionary<string, string>();
        if (missing.Count == 0)
            return result;

        var stored = LoadStore();
        var dirty = false;

        foreach (var key in missing)
        {
            if (stored.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                result[key] = value;
                continue;
            }

            var generated = GenerateSecret();
            stored[key] = generated;
            result[key] = generated;
            dirty = true;
            _logger.LogInformation("Generated a new Local secret for configuration key {Key} (value not logged).", key);
        }

        if (dirty)
            SaveStore(stored);

        return result;
    }

    private static string GenerateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private Dictionary<string, string> LoadStore()
    {
        if (!File.Exists(_storePath))
            return new Dictionary<string, string>();

        try
        {
            var bytes = File.ReadAllBytes(_storePath);
            var json = OperatingSystem.IsWindows() ? UnprotectWindows(bytes) : Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            // A corrupt/unreadable store must never crash startup or silently fall back to a
            // shared default — treat it as empty so missing keys regenerate fresh secrets.
            _logger.LogWarning(ex, "Could not read the Local secrets store at {Path}; new secrets will be generated.", _storePath);
            return new Dictionary<string, string>();
        }
    }

    private void SaveStore(Dictionary<string, string> store)
    {
        Directory.CreateDirectory(_storeDirectory);
        var json = JsonSerializer.Serialize(store);
        var bytes = OperatingSystem.IsWindows() ? ProtectWindows(json) : Encoding.UTF8.GetBytes(json);

        if (!OperatingSystem.IsWindows())
            _logger.LogWarning("Local secrets store is being written without OS-level protection (non-Windows host). Windows deployments use DPAPI.");

        // Write-to-temp-then-move so a crash mid-write can never leave a corrupt/partial store.
        var tempPath = _storePath + ".tmp";
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, _storePath, overwrite: true);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(string json) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.LocalMachine);

    [SupportedOSPlatform("windows")]
    private static string UnprotectWindows(byte[] bytes) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.LocalMachine));
}
