namespace GMS.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using GMS.Infrastructure.Configuration;

/// <summary>
/// Local Edition per-install secret bootstrap. Each test uses its own temp directory so runs
/// never interfere with each other or with a real %ProgramData%\HyMotion\config install.
/// </summary>
public class LocalSecretProviderTests : IDisposable
{
    private readonly string _dir;

    public LocalSecretProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hymotion-secret-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private LocalSecretProvider NewProvider() => new(NullLogger<LocalSecretProvider>.Instance, _dir);

    private static readonly string[] Keys = { "JwtSettings:SecretKey", "EncryptionKey" };

    [Fact]
    public void MissingSecret_IsGenerated()
    {
        var provider = NewProvider();
        var existing = new Dictionary<string, string?>();

        var result = provider.EnsureSecrets(Keys, existing);

        Assert.Equal(2, result.Count);
        Assert.False(string.IsNullOrWhiteSpace(result["JwtSettings:SecretKey"]));
        Assert.False(string.IsNullOrWhiteSpace(result["EncryptionKey"]));
        Assert.NotEqual(result["JwtSettings:SecretKey"], result["EncryptionKey"]);
    }

    [Fact]
    public void Restart_ReusesSameGeneratedSecret()
    {
        var first = NewProvider().EnsureSecrets(Keys, new Dictionary<string, string?>());

        // A fresh provider instance simulates a process restart reading the same on-disk store.
        var second = NewProvider().EnsureSecrets(Keys, new Dictionary<string, string?>());

        Assert.Equal(first["JwtSettings:SecretKey"], second["JwtSettings:SecretKey"]);
        Assert.Equal(first["EncryptionKey"], second["EncryptionKey"]);
    }

    [Fact]
    public void ExplicitlyConfiguredValue_IsNeverOverriddenOrReturned()
    {
        var existing = new Dictionary<string, string?> { ["EncryptionKey"] = "operator-supplied-value" };

        var result = NewProvider().EnsureSecrets(Keys, existing);

        Assert.True(result.ContainsKey("JwtSettings:SecretKey"));
        Assert.False(result.ContainsKey("EncryptionKey"));
    }

    [Fact]
    public void AllKeysAlreadyConfigured_TouchesNothingOnDisk()
    {
        var existing = new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = "a",
            ["EncryptionKey"] = "b",
        };

        var result = NewProvider().EnsureSecrets(Keys, existing);

        Assert.Empty(result);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void GeneratedSecrets_AreNotStoredAsPlaintextOnDisk_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return; // DPAPI protection (this test's subject) is Windows-only; see LocalSecretProvider remarks.

        NewProvider().EnsureSecrets(Keys, new Dictionary<string, string?>());

        var filePath = Directory.GetFiles(_dir).Single();
        var raw = File.ReadAllText(filePath);

        Assert.DoesNotContain("JwtSettings", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("EncryptionKey", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondCall_OnlyGeneratesForStillMissingKeys()
    {
        var provider = NewProvider();
        var afterFirst = provider.EnsureSecrets(new[] { "EncryptionKey" }, new Dictionary<string, string?>());

        // Second call: EncryptionKey now "configured" (as if it round-tripped into IConfiguration),
        // JwtSettings:SecretKey still missing — only the latter should be freshly generated.
        var existing = new Dictionary<string, string?> { ["EncryptionKey"] = afterFirst["EncryptionKey"] };
        var afterSecond = provider.EnsureSecrets(Keys, existing);

        Assert.False(afterSecond.ContainsKey("EncryptionKey"));
        Assert.True(afterSecond.ContainsKey("JwtSettings:SecretKey"));
    }
}
