namespace GMS.Tests;

using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Auth;
using GMS.Infrastructure.Configuration;

/// <summary>
/// Keep-signed-in store is auth-only: gym identity is never in this file, passwords are never a
/// field, and a session bound to the wrong gym/installation is discarded.
/// </summary>
public class LocalDeviceSessionStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public LocalDeviceSessionStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hymotion-device-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "device-session.dat");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private LocalDeviceSessionStore NewStore() =>
        new(NullLogger<LocalDeviceSessionStore>.Instance, _path);

    private static LocalDeviceSessionFileContent Sample(string token = "refresh-abc", string gym = "GYM-GYM-4191", string install = "INS-D6A0055EEE") =>
        new()
        {
            RefreshToken = token,
            GymCode = gym,
            InstallationId = install,
            TenantId = Guid.Parse("870a21e6-fad6-46b3-a1b6-ff45252f3486"),
            SavedAtUtc = DateTime.UtcNow,
        };

    [Fact]
    public void Restart_RestoresRefreshToken_WithoutPassword()
    {
        NewStore().Save(Sample());

        var loaded = NewStore().Load();

        Assert.NotNull(loaded);
        Assert.Equal("refresh-abc", loaded!.RefreshToken);
        Assert.Equal("GYM-GYM-4191", loaded.GymCode);
        Assert.Equal("INS-D6A0055EEE", loaded.InstallationId);
        Assert.Null(typeof(LocalDeviceSessionFileContent).GetProperty("Password", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(LocalDeviceSessionFileContent).GetProperty("Email", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(LoginResponse).GetProperty("Password"));
        Assert.Null(typeof(UserInfo).GetProperty("Password"));
        Assert.Null(typeof(SaveDeviceSessionRequest).GetProperty("Password"));
    }

    [Fact]
    public void LoadIfBound_WrongGym_ClearsSession()
    {
        NewStore().Save(Sample());

        var loaded = NewStore().LoadIfBound("GYM-OTHER", "INS-D6A0055EEE");

        Assert.Null(loaded);
        Assert.Null(NewStore().Load());
    }

    [Fact]
    public void LoadIfBound_WrongInstallation_ClearsSession()
    {
        NewStore().Save(Sample());

        var loaded = NewStore().LoadIfBound("GYM-GYM-4191", "INS-OTHER");

        Assert.Null(loaded);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void LoadIfBound_MatchingGym_IsCaseInsensitive()
    {
        NewStore().Save(Sample());

        var loaded = NewStore().LoadIfBound("gym-gym-4191", "INS-D6A0055EEE");

        Assert.NotNull(loaded);
        Assert.Equal("refresh-abc", loaded!.RefreshToken);
    }

    [Fact]
    public void LoadIfBound_MissingLiveGym_ClearsSession()
    {
        NewStore().Save(Sample());

        Assert.Null(NewStore().LoadIfBound(null, "INS-D6A0055EEE"));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Clear_DoesNotThrow_WhenFileMissing()
    {
        NewStore().Clear();
        NewStore().Clear();
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void SerializedPayload_HasNoPasswordProperty()
    {
        var json = JsonSerializer.Serialize(Sample());
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RefreshToken", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_OnWindows_DoesNotWriteRefreshTokenInPlaintext()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const string secret = "refresh-plaintext-must-not-appear";
        NewStore().Save(Sample(token: secret));

        var bytes = File.ReadAllBytes(_path);
        var needle = Encoding.UTF8.GetBytes(secret);
        Assert.True(IndexOf(bytes, needle) < 0);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }
}
