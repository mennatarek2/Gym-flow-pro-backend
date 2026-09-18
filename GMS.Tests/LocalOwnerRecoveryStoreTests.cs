namespace GMS.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using GMS.Infrastructure.Configuration;

public class LocalOwnerRecoveryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public LocalOwnerRecoveryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hymotion-owner-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "owner-recovery.dat");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Restart_RestoresRequestWithoutPassword()
    {
        var store = new LocalOwnerRecoveryStore(NullLogger<LocalOwnerRecoveryStore>.Instance, _path);
        store.Save(new LocalOwnerRecoveryFileContent
        {
            RequestId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            InstallationId = "INS-1",
            GymCode = "GYM-1",
            Nonce = "N",
            Status = "pending",
            CreatedAtUtc = DateTime.UtcNow,
            ChallengeText = "HYMR1.abc",
        });

        var loaded = new LocalOwnerRecoveryStore(NullLogger<LocalOwnerRecoveryStore>.Instance, _path).Load();
        Assert.NotNull(loaded);
        Assert.Equal("GYM-1", loaded!.GymCode);
        Assert.Null(typeof(LocalOwnerRecoveryFileContent).GetProperty("Password"));
        Assert.Null(typeof(LocalOwnerRecoveryFileContent).GetProperty("LicenseSigningPrivateKeyPem"));
    }

    [Fact]
    public void Completed_ClearsApprovalToken()
    {
        var store = new LocalOwnerRecoveryStore(NullLogger<LocalOwnerRecoveryStore>.Instance, _path);
        store.Save(new LocalOwnerRecoveryFileContent
        {
            RequestId = Guid.NewGuid(),
            InstallationId = "INS-1",
            GymCode = "GYM-1",
            Nonce = "N",
            Status = "completed",
            ApprovalToken = "HYMA1.should-not-persist",
            CreatedAtUtc = DateTime.UtcNow,
        });
        var loaded = store.Load();
        Assert.Null(loaded!.ApprovalToken);
    }
}
