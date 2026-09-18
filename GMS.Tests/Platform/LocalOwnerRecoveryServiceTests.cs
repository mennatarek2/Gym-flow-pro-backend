namespace GMS.Tests.Platform;

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using GMS.Core.Licensing;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

public class LocalOwnerRecoveryServiceTests
{
    private static PlatformDbContext NewDb() =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase("owner-rec-" + Guid.NewGuid())
            .Options);

    private static ILicenseSigningService NewSigner(out string publicKeyPem)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privatePem = ec.ExportECPrivateKeyPem();
        publicKeyPem = ec.ExportSubjectPublicKeyInfoPem();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LicenseSigning:PrivateKeyPem"] = privatePem })
            .Build();
        return new LicenseSigningService(config);
    }

    private static bool Verify(string publicKeyPem, OwnerRecoveryPayload payload, string signature)
    {
        using var ec = ECDsa.Create();
        ec.ImportFromPem(publicKeyPem);
        return ec.VerifyData(
            OwnerRecoveryCanonicalizer.Build(payload),
            Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256);
    }

    private static (LocalOwnerRecoveryService svc, PlatformDbContext db, RecordingAudit audit, string publicKeyPem, LocalLicense license, LocalInstallation install)
        NewSut()
    {
        var db = NewDb();
        var customer = new PlatformCustomer { BusinessName = "Gold", OwnerName = "Owner" };
        db.Customers.Add(customer);
        var license = new LocalLicense
        {
            LicenseKey = "HY-LCL-RECOV-TEST1",
            CustomerName = "Gold",
            CustomerId = customer.Id,
            Status = "active",
            DeviceLimit = 1,
        };
        db.LocalLicenses.Add(license);
        var install = new LocalInstallation
        {
            LicenseId = license.Id,
            InstallationId = "INS-RECOV01",
            Status = "active",
            GymCode = "GYM-GOLD",
            GymName = "Gold",
        };
        db.LocalInstallations.Add(install);
        db.SaveChanges();
        license.Installations.Add(install);
        var signer = NewSigner(out var pem);
        var audit = new RecordingAudit();
        return (new LocalOwnerRecoveryService(db, signer, audit), db, audit, pem, license, install);
    }

    private static LocalOwnerRecoveryAuthRequest Auth(
        LocalLicense license,
        LocalInstallation install,
        Guid? requestId = null,
        string? nonce = null,
        string? gymCode = null) => new()
    {
        LicenseKey = license.LicenseKey,
        InstallationId = install.InstallationId,
        RequestId = requestId,
        GymCode = gymCode ?? install.GymCode,
        GymName = install.GymName,
        Nonce = nonce,
    };

    [Fact]
    public async Task Request_CreatesPendingBoundToInstallation()
    {
        var (svc, _, audit, _, license, install) = NewSut();
        var requestId = Guid.NewGuid();
        var result = await svc.RequestFromInstallationAsync(Auth(license, install, requestId, "AABBCC"), "127.0.0.1");
        Assert.Equal(requestId, result.RequestId);
        Assert.Equal(LocalOwnerRecoveryStatuses.Pending, result.Status);
        Assert.Null(result.RecoveryCode);
        Assert.Contains(audit.Actions, a => a == "platform.local_owner_recovery.requested");
        Assert.DoesNotContain(audit.Payloads, p => p.Contains("HYMA1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Request_WrongGym_IsRejected()
    {
        var (svc, _, _, _, license, install) = NewSut();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), "NN", "GYM-OTHER"), null));
    }

    [Fact]
    public async Task Request_UnknownInstallation_IsRejectedWithoutLeaking()
    {
        var (svc, _, _, _, license, install) = NewSut();
        var req = Auth(license, install, Guid.NewGuid(), "NN");
        req.InstallationId = "INS-NOPE";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RequestFromInstallationAsync(req, null));
        Assert.Equal("This recovery request could not be submitted.", ex.Message);
    }

    [Fact]
    public async Task Approve_IssuesOneTimeSignedBlobBoundToRequest()
    {
        var (svc, _, audit, pem, license, install) = NewSut();
        var started = await svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), "NONCE1"), null);
        var approved = await svc.ApproveAsync(started.RequestId, new OwnerRecoveryDecisionRequest
        {
            Reason = "Verified owner via WhatsApp call",
            Method = "online",
        }, Guid.NewGuid());

        Assert.Equal(LocalOwnerRecoveryStatuses.Approved, approved.Status);
        Assert.False(string.IsNullOrWhiteSpace(approved.RecoveryCode));
        Assert.StartsWith("HYMA1.", approved.RecoveryCode);
        Assert.True(OwnerRecoveryCodec.TryParseApproval(approved.RecoveryCode, out var payload, out var sig));
        Assert.True(Verify(pem, payload, sig));
        Assert.Equal(install.InstallationId, payload.InstallationId);
        Assert.Equal("GYM-GOLD", payload.GymCode);
        Assert.Equal("NONCE1", payload.Nonce);
        Assert.Equal(started.RequestId, payload.RequestId);
        Assert.DoesNotContain(audit.Payloads, p => p.Contains(approved.RecoveryCode!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Approve_ShortReason_Rejected()
    {
        var (svc, _, _, _, license, install) = NewSut();
        var started = await svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), "N"), null);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.ApproveAsync(started.RequestId, new OwnerRecoveryDecisionRequest { Reason = "short" }, Guid.NewGuid()));
    }

    [Fact]
    public async Task RejectThenApprove_IsInvalidTransition()
    {
        var (svc, _, _, _, license, install) = NewSut();
        var started = await svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), "N"), null);
        await svc.RejectAsync(started.RequestId, new OwnerRecoveryDecisionRequest { Reason = "Could not verify caller" }, Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApproveAsync(started.RequestId, new OwnerRecoveryDecisionRequest { Reason = "Trying again after reject" }, Guid.NewGuid()));
    }

    [Fact]
    public async Task Poll_ReturnsCodeOnlyWhenApproved_AndCompleteIsIdempotent()
    {
        var (svc, _, _, _, license, install) = NewSut();
        var nonce = "POLLNONCE";
        var started = await svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), nonce), null);
        var pendingPoll = await svc.PollFromInstallationAsync(Auth(license, install, started.RequestId, nonce));
        Assert.Null(pendingPoll.RecoveryCode);

        var approved = await svc.ApproveAsync(started.RequestId, new OwnerRecoveryDecisionRequest
        {
            Reason = "Verified owner in person at the gym",
        }, Guid.NewGuid());
        Assert.True(OwnerRecoveryCodec.TryParseApproval(approved.RecoveryCode, out var payload, out _));

        var polled = await svc.PollFromInstallationAsync(Auth(license, install, started.RequestId, nonce));
        Assert.Equal(LocalOwnerRecoveryStatuses.Approved, polled.Status);
        Assert.Equal(approved.RecoveryCode, polled.RecoveryCode);

        var completeReq = Auth(license, install, started.RequestId, nonce);
        completeReq.Jti = payload.Jti;
        var first = await svc.CompleteFromInstallationAsync(completeReq, null);
        var second = await svc.CompleteFromInstallationAsync(completeReq, null);
        Assert.Equal(LocalOwnerRecoveryStatuses.Completed, first.Status);
        Assert.Equal(LocalOwnerRecoveryStatuses.Completed, second.Status);
        Assert.Null(first.RecoveryCode);
        Assert.Null(second.RecoveryCode);
    }

    [Fact]
    public async Task Complete_WrongJti_Fails()
    {
        var (svc, _, _, _, license, install) = NewSut();
        var started = await svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), "N"), null);
        await svc.ApproveAsync(started.RequestId, new OwnerRecoveryDecisionRequest { Reason = "Verified owner on a recorded call" }, Guid.NewGuid());
        var req = Auth(license, install, started.RequestId, "N");
        req.Jti = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CompleteFromInstallationAsync(req, null));
    }

    [Fact]
    public async Task CrossInstallation_Poll_Fails()
    {
        var (svc, db, _, _, license, install) = NewSut();
        var other = new LocalInstallation
        {
            LicenseId = license.Id,
            InstallationId = "INS-OTHER99",
            Status = "active",
            GymCode = "GYM-GOLD",
        };
        db.LocalInstallations.Add(other);
        db.SaveChanges();
        license.Installations.Add(other);

        var started = await svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), "N"), null);
        var steal = Auth(license, other, started.RequestId, "N");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PollFromInstallationAsync(steal));
    }

    [Fact]
    public async Task ImportChallengeThenOfflineApprove_BindsNonce()
    {
        var (svc, _, _, pem, _, install) = NewSut();
        var challenge = new OwnerRecoveryChallenge
        {
            RequestId = Guid.NewGuid(),
            InstallationId = install.InstallationId,
            GymCode = install.GymCode!,
            GymName = install.GymName,
            Nonce = "OFFLINE-NONCE",
            IssuedAtUtc = DateTime.UtcNow,
        };
        var imported = await svc.ImportChallengeAsync(
            new ImportOwnerRecoveryChallengeRequest { Challenge = OwnerRecoveryCodec.FormatChallenge(challenge) },
            Guid.NewGuid());
        Assert.Equal(LocalOwnerRecoveryStatuses.Pending, imported.Status);

        var approved = await svc.ApproveAsync(imported.Id, new OwnerRecoveryDecisionRequest
        {
            Reason = "Owner read the gym code from the PC screen",
            Method = "offline",
        }, Guid.NewGuid());
        Assert.Equal("offline", approved.Method);
        Assert.True(OwnerRecoveryCodec.TryParseApproval(approved.RecoveryCode, out var payload, out var sig));
        Assert.True(Verify(pem, payload, sig));
        Assert.Equal("OFFLINE-NONCE", payload.Nonce);
    }

    [Fact]
    public async Task DuplicateInstallation_CustomerScope_ShowsAndImportsGoldRequest()
    {
        var (svc, db, _, _, goldLicense, goldInstall) = NewSut();
        var running = new PlatformCustomer { BusinessName = "running", OwnerName = "Other" };
        db.Customers.Add(running);
        var runningLicense = new LocalLicense
        {
            LicenseKey = "HY-LCL-RUNNING",
            CustomerName = "running",
            CustomerId = running.Id,
            Status = "active",
            DeviceLimit = 1,
        };
        db.LocalLicenses.Add(runningLicense);
        var runningInstall = new LocalInstallation
        {
            LicenseId = runningLicense.Id,
            InstallationId = goldInstall.InstallationId,
            Status = "active",
            GymCode = goldInstall.GymCode,
            GymName = goldInstall.GymName,
            LastValidatedAtUtc = DateTime.UtcNow,
        };
        db.LocalInstallations.Add(runningInstall);

        var swim = new PlatformCustomer { BusinessName = "Swimming Academy", OwnerName = "Swim" };
        db.Customers.Add(swim);
        var swimLicense = new LocalLicense
        {
            LicenseKey = "HY-LCL-SWIM",
            CustomerName = "Swim",
            CustomerId = swim.Id,
            Status = "active",
            DeviceLimit = 1,
        };
        db.LocalLicenses.Add(swimLicense);
        db.LocalInstallations.Add(new LocalInstallation
        {
            LicenseId = swimLicense.Id,
            InstallationId = goldInstall.InstallationId,
            Status = "active",
            LastValidatedAtUtc = DateTime.UtcNow.AddHours(1),
        });
        db.SaveChanges();

        var started = await svc.RequestFromInstallationAsync(
            Auth(runningLicense, runningInstall, Guid.NewGuid(), "NONCE-GOLD"),
            null);
        var stored = await svc.GetAsync(started.RequestId, false);
        Assert.Equal(running.Id, stored!.CustomerId);

        var onGold = await svc.ListAsync(goldLicense.CustomerId, null);
        Assert.Contains(onGold, x => x.Id == started.RequestId);

        var onSwim = await svc.ListAsync(swim.Id, null);
        Assert.DoesNotContain(onSwim, x => x.Id == started.RequestId);

        var challenge = new OwnerRecoveryChallenge
        {
            RequestId = started.RequestId,
            InstallationId = goldInstall.InstallationId,
            GymCode = goldInstall.GymCode!,
            GymName = goldInstall.GymName,
            Nonce = "NONCE-GOLD",
            IssuedAtUtc = DateTime.UtcNow,
        };
        var imported = await svc.ImportChallengeAsync(
            new ImportOwnerRecoveryChallengeRequest
            {
                Challenge = OwnerRecoveryCodec.FormatChallenge(challenge),
                CustomerId = goldLicense.CustomerId,
            },
            Guid.NewGuid());
        Assert.Equal(started.RequestId, imported.Id);

        var steal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ImportChallengeAsync(
                new ImportOwnerRecoveryChallengeRequest
                {
                    Challenge = OwnerRecoveryCodec.FormatChallenge(challenge),
                    CustomerId = swim.Id,
                },
                Guid.NewGuid()));
        Assert.Contains("does not belong to this customer", steal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gold", steal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TamperedApproval_FailsVerify()
    {
        var (svc, _, _, pem, license, install) = NewSut();
        var started = await svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), "N"), null);
        var approved = await svc.ApproveAsync(started.RequestId, new OwnerRecoveryDecisionRequest { Reason = "Verified caller against customer file" }, Guid.NewGuid());
        Assert.True(OwnerRecoveryCodec.TryParseApproval(approved.RecoveryCode, out var payload, out var sig));
        payload.GymCode = "GYM-EVIL";
        Assert.False(Verify(pem, payload, sig));
    }

    [Fact]
    public async Task NewRequest_CancelsPreviousPending_ButNotOutstandingApproval()
    {
        var (svc, _, _, _, license, install) = NewSut();
        var first = await svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), "A"), null);
        var second = await svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), "B"), null);
        Assert.Equal(LocalOwnerRecoveryStatuses.Pending, second.Status);
        var old = await svc.GetAsync(first.RequestId, false);
        Assert.Equal(LocalOwnerRecoveryStatuses.Cancelled, old!.Status);

        await svc.ApproveAsync(second.RequestId, new OwnerRecoveryDecisionRequest { Reason = "Verified owner, issuing approval now" }, Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), "C"), null));
    }

    [Fact]
    public async Task Revoke_PreventsComplete()
    {
        var (svc, _, _, _, license, install) = NewSut();
        var started = await svc.RequestFromInstallationAsync(Auth(license, install, Guid.NewGuid(), "N"), null);
        var approved = await svc.ApproveAsync(started.RequestId, new OwnerRecoveryDecisionRequest { Reason = "Verified then caller hung up" }, Guid.NewGuid());
        await svc.RevokeAsync(started.RequestId, new OwnerRecoveryDecisionRequest { Reason = "Caller failed follow-up questions" }, Guid.NewGuid());
        Assert.True(OwnerRecoveryCodec.TryParseApproval(approved.RecoveryCode, out var payload, out _));
        var req = Auth(license, install, started.RequestId, "N");
        req.Jti = payload.Jti;
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CompleteFromInstallationAsync(req, null));
    }

    private sealed class RecordingAudit : IPlatformAuditService
    {
        public List<string> Actions { get; } = [];
        public List<string> Payloads { get; } = [];

        public Task LogAsync(Guid actorPlatformUserId, string action, Guid? tenantId = null, object? before = null, object? after = null, string? ipAddress = null)
        {
            Actions.Add(action);
            Payloads.Add(System.Text.Json.JsonSerializer.Serialize(new { before, after }));
            return Task.CompletedTask;
        }

        public Task<PlatformPagedResult<PlatformAuditLogDto>> ListAsync(Guid? tenantId, string? action, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformPagedResult<PlatformAuditLogDto> { Items = [], TotalCount = 0, Page = page, PageSize = pageSize });
    }
}

public class OwnerRecoveryCodecTests
{
    [Fact]
    public void Challenge_RoundTrip()
    {
        var challenge = new OwnerRecoveryChallenge
        {
            RequestId = Guid.NewGuid(),
            InstallationId = "INS-1",
            GymCode = "GYM-1",
            Nonce = "ABC",
            IssuedAtUtc = DateTime.UtcNow,
        };
        var text = "Please help\n" + OwnerRecoveryCodec.FormatChallenge(challenge) + "\nthanks";
        Assert.True(OwnerRecoveryCodec.TryParseChallenge(text, out var parsed));
        Assert.Equal(challenge.RequestId, parsed.RequestId);
        Assert.Equal(challenge.Nonce, parsed.Nonce);
    }

    [Fact]
    public void ClockSkew_AllowsTwoMinutes()
    {
        var expires = DateTime.UtcNow.AddMinutes(-1);
        Assert.False(OwnerRecoveryCodec.IsExpired(expires, DateTime.UtcNow));
        Assert.True(OwnerRecoveryCodec.IsExpired(expires.AddMinutes(-2), DateTime.UtcNow));
    }
}
