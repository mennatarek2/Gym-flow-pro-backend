namespace GMS.Tests.Platform;

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using GMS.Core.Licensing;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

/// <summary>
/// Core HyMotion Local licensing tests — the anti-resale mechanism (rule 16) is the single most
/// important behavior in this whole feature; see ActivateSameLicense_FromDifferentInstallation_
/// IsRejected_WhenDeviceLimitReached below.
/// </summary>
public class LocalLicenseServiceTests
{
    private static PlatformDbContext NewDb() =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase("local-lic-" + Guid.NewGuid())
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

    private static (LocalLicenseService service, PlatformDbContext db, string publicKeyPem, Guid customerId) NewSut()
    {
        var db = NewDb();
        var customer = new PlatformCustomer { BusinessName = "Fitness Hub", OwnerName = "Owner" };
        db.Customers.Add(customer);
        db.SaveChanges();
        var repo = new LocalLicenseWriteRepository(db);
        var signer = NewSigner(out var publicKeyPem);
        return (new LocalLicenseService(repo, signer, db), db, publicKeyPem, customer.Id);
    }

    private static IssueLocalLicenseRequest IssueReq(Guid customerId, int deviceLimit = 1) =>
        new() { CustomerId = customerId, DeviceLimit = deviceLimit };

    private static bool VerifyWithPublicKey(string publicKeyPem, LocalLicensePayload payload)
    {
        using var ec = ECDsa.Create();
        ec.ImportFromPem(publicKeyPem);
        var data = LicenseCanonicalizer.Build(payload);
        return ec.VerifyData(data, Convert.FromBase64String(payload.Signature), HashAlgorithmName.SHA256);
    }

    private static Guid AdminId => Guid.NewGuid();

    // ── Signing / tamper detection ──

    [Fact]
    public async Task TamperedPayload_FailsVerification()
    {
        var (svc, _, publicKeyPem, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);

        var result = await svc.ActivateAsync(license.LicenseKey, "INS-0001", null, "127.0.0.1");
        Assert.True(result.Success);

        var payload = result.License!;
        Assert.True(VerifyWithPublicKey(publicKeyPem, payload));

        // Tamper with device limit after the fact — a classic "unlock more devices locally" attack.
        payload.DeviceLimit = 999;
        Assert.False(VerifyWithPublicKey(publicKeyPem, payload));
    }

    // ── Basic lifecycle ──

    [Fact]
    public async Task IssueAsync_CreatesLicenseInPendingActivation()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);

        Assert.Equal(LocalLicenseStatuses.PendingActivation, license.Status);
        Assert.Equal(customerId, license.CustomerId);
        Assert.Equal("Fitness Hub", license.CustomerName);
        Assert.StartsWith("HY-LCL-", license.LicenseKey);
    }

    [Fact]
    public async Task IssueAsync_WithoutCustomerId_Throws()
    {
        var (svc, _, _, _) = NewSut();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.IssueAsync(new IssueLocalLicenseRequest { CustomerName = "Fitness Hub", DeviceLimit = 1 }, AdminId));
    }

    [Fact]
    public async Task IssueAsync_UnknownCustomerId_Throws()
    {
        var (svc, _, _, _) = NewSut();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.IssueAsync(new IssueLocalLicenseRequest { CustomerId = Guid.NewGuid(), DeviceLimit = 1 }, AdminId));
    }

    [Fact]
    public async Task IssueAsync_UnknownCustomerId_MessageIsCustomerNotFound()
    {
        var (svc, _, _, _) = NewSut();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.IssueAsync(new IssueLocalLicenseRequest { CustomerId = Guid.NewGuid(), DeviceLimit = 1 }, AdminId));
        Assert.Equal("Customer was not found.", ex.Message);
    }

    [Fact]
    public async Task IssueAsync_CancelledContract_Throws()
    {
        var (svc, db, _, customerId) = NewSut();
        var contract = new PlatformContract
        {
            CustomerId = customerId,
            ContractNumber = "HY-CTR-2026-0099",
            Status = PlatformContractStatuses.Cancelled,
            Currency = "EGP",
            PaymentStatus = PlatformContractPaymentStatuses.Unpaid,
            CreatedByPlatformAdminUserId = AdminId,
            ContractDate = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.IssueAsync(new IssueLocalLicenseRequest
            {
                CustomerId = customerId,
                ContractId = contract.Id,
                DeviceLimit = 1,
            }, AdminId));
        Assert.Equal("Cannot issue a license against a cancelled contract.", ex.Message);
    }

    [Fact]
    public async Task ActivateAsync_ValidLicense_Succeeds_AndBindsInstallation()
    {
        var (svc, _, publicKeyPem, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);

        var result = await svc.ActivateAsync(license.LicenseKey, "INS-0001", "fp-1", "1.1.1.1");

        Assert.True(result.Success);
        Assert.Equal(LocalActivationResults.Success, result.Result);
        Assert.NotNull(result.License);
        Assert.Equal("INS-0001", result.License!.InstallationId);
        Assert.True(VerifyWithPublicKey(publicKeyPem, result.License));

        var detail = await svc.GetDetailAsync(license.Id);
        Assert.Equal(LocalLicenseStatuses.Active, detail!.Status);
        Assert.Equal(1, detail.ActiveInstallationCount);
        Assert.Equal(LocalInstallationStatuses.Active, detail.InstallationStatus);
        Assert.NotNull(detail.LastValidatedAtUtc);

        var list = await svc.ListAsync();
        var row = Assert.Single(list);
        Assert.Equal(license.Id, row.Id);
        Assert.Equal(LocalLicenseStatuses.Active, row.Status);
        Assert.Equal(1, row.ActiveInstallationCount);
        Assert.Equal(LocalInstallationStatuses.Active, row.InstallationStatus);
        Assert.NotNull(row.LastValidatedAtUtc);
        Assert.Equal(detail.LastValidatedAtUtc, row.LastValidatedAtUtc);
    }

    [Fact]
    public async Task ListAsync_IssuedLicense_HasNoInstallationCheckIn()
    {
        var (svc, _, _, customerId) = NewSut();
        await svc.IssueAsync(IssueReq(customerId), AdminId);

        var row = Assert.Single(await svc.ListAsync());
        Assert.Equal(LocalLicenseStatuses.PendingActivation, row.Status);
        Assert.Equal(0, row.ActiveInstallationCount);
        Assert.Null(row.LastValidatedAtUtc);
        Assert.Null(row.InstallationStatus);
    }

    [Fact]
    public async Task CheckAsync_DoesNotMarkLicenseActiveOrConsumeSlot()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);

        var check = await svc.CheckAsync(license.LicenseKey, "INS-0001", "1.1.1.1");

        Assert.True(check.Success);
        Assert.Equal(LocalActivationResults.CheckSuccess, check.Result);
        Assert.Null(check.License);

        var detail = await svc.GetDetailAsync(license.Id);
        Assert.Equal(LocalLicenseStatuses.PendingActivation, detail!.Status);
        Assert.Equal(0, detail.ActiveInstallationCount);
    }

    [Fact]
    public async Task CheckAsync_DeviceLimitExceeded_DoesNotBindSecondInstallation()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);
        Assert.True((await svc.ActivateAsync(license.LicenseKey, "INS-GYM-A", null, "1.1.1.1")).Success);

        var check = await svc.CheckAsync(license.LicenseKey, "INS-GYM-B", "2.2.2.2");

        Assert.False(check.Success);
        Assert.Equal(LocalActivationResults.DeviceLimitExceeded, check.Result);

        var detail = await svc.GetDetailAsync(license.Id);
        Assert.Equal(1, detail!.ActiveInstallationCount);
        Assert.DoesNotContain(detail.Installations, i => i.InstallationId == "INS-GYM-B");
    }

    [Fact]
    public async Task ReleaseAsync_AfterActivate_ReturnsLicenseToPending()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);
        Assert.True((await svc.ActivateAsync(license.LicenseKey, "INS-0001", null, "1.1.1.1")).Success);

        var released = await svc.ReleaseAsync(license.LicenseKey, "INS-0001", "1.1.1.1");

        Assert.True(released.Success);
        var detail = await svc.GetDetailAsync(license.Id);
        Assert.Equal(LocalLicenseStatuses.PendingActivation, detail!.Status);
        Assert.Equal(0, detail.ActiveInstallationCount);
    }

    [Fact]
    public async Task ActivateAsync_InvalidLicenseKey_Rejected()
    {
        var (svc, _, _, customerId) = NewSut();
        var result = await svc.ActivateAsync("HY-LCL-NOPE0-00000", "INS-0001", null, "1.1.1.1");

        Assert.False(result.Success);
        Assert.Equal(LocalActivationResults.InvalidLicenseKey, result.Result);
    }

    [Fact]
    public async Task ActivateAsync_SameInstallationTwice_DoesNotConsumeExtraDeviceSlot()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);

        var first = await svc.ActivateAsync(license.LicenseKey, "INS-0001", null, "1.1.1.1");
        var second = await svc.ActivateAsync(license.LicenseKey, "INS-0001", null, "1.1.1.1");

        Assert.True(first.Success);
        Assert.True(second.Success); // reinstalling on the SAME PC must always work

        var detail = await svc.GetDetailAsync(license.Id);
        Assert.Equal(1, detail!.ActiveInstallationCount); // still just one slot used
    }

    // ── THE anti-resale test (rule 16) ──

    [Fact]
    public async Task ActivateSameLicense_FromDifferentInstallation_IsRejected_WhenDeviceLimitReached()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);

        // Gym A activates first.
        var gymA = await svc.ActivateAsync(license.LicenseKey, "INS-GYM-A", null, "1.1.1.1");
        Assert.True(gymA.Success);

        // Gym B copies the installer + the same license key and tries to activate on a
        // different PC. This must fail outright — this is the literal scenario rule 16 exists
        // to prevent.
        var gymB = await svc.ActivateAsync(license.LicenseKey, "INS-GYM-B", null, "2.2.2.2");

        Assert.False(gymB.Success);
        Assert.Equal(LocalActivationResults.DeviceLimitExceeded, gymB.Result);

        // Gym A's own installation must remain completely unaffected by Gym B's rejected attempt.
        var detail = await svc.GetDetailAsync(license.Id);
        Assert.Equal(1, detail!.ActiveInstallationCount);
        Assert.Contains(detail.Installations, i => i.InstallationId == "INS-GYM-A" && i.Status == "active");
        Assert.DoesNotContain(detail.Installations, i => i.InstallationId == "INS-GYM-B");
    }

    [Fact]
    public async Task DeviceLimit_AllowsExactlyThatManyConcurrentInstallations()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId, 2), AdminId);

        var first = await svc.ActivateAsync(license.LicenseKey, "INS-BRANCH-1", null, "1.1.1.1");
        var second = await svc.ActivateAsync(license.LicenseKey, "INS-BRANCH-2", null, "1.1.1.2");
        var third = await svc.ActivateAsync(license.LicenseKey, "INS-BRANCH-3", null, "1.1.1.3");

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.False(third.Success);
        Assert.Equal(LocalActivationResults.DeviceLimitExceeded, third.Result);
    }

    // ── Revocation / suspension ──

    [Fact]
    public async Task ActivateAsync_RevokedLicense_Rejected()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);
        await svc.ActivateAsync(license.LicenseKey, "INS-0001", null, "1.1.1.1");

        var revoked = await svc.RevokeAsync(license.Id, AdminId, "Chargeback");
        Assert.True(revoked);

        var attempt = await svc.ActivateAsync(license.LicenseKey, "INS-0002", null, "1.1.1.1");
        Assert.False(attempt.Success);
        Assert.Equal(LocalActivationResults.LicenseRevoked, attempt.Result);
    }

    [Fact]
    public async Task ActivateAsync_SuspendedLicense_Rejected()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);
        await svc.ActivateAsync(license.LicenseKey, "INS-0001", null, "1.1.1.1");
        await svc.SuspendAsync(license.Id, AdminId, "Payment dispute under review");

        var attempt = await svc.ValidateAsync(license.LicenseKey, "INS-0001", "1.1.1.1");
        Assert.False(attempt.Success);
        Assert.Equal(LocalActivationResults.LicenseSuspended, attempt.Result);
    }

    [Fact]
    public async Task ReactivateAsync_RequiresReason_AndOnlyWorksFromRevokedOrSuspended()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);

        // Cannot reactivate a license that was never revoked/suspended.
        var reactivateNotRevoked = await svc.ReactivateAsync(license.Id, AdminId, "test");
        Assert.False(reactivateNotRevoked);

        await svc.RevokeAsync(license.Id, AdminId, "mistake");
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ReactivateAsync(license.Id, AdminId, ""));

        var reactivated = await svc.ReactivateAsync(license.Id, AdminId, "Chargeback reversed by bank");
        Assert.True(reactivated);

        var detail = await svc.GetDetailAsync(license.Id);
        Assert.Equal(LocalLicenseStatuses.Active, detail!.Status);
    }

    // ── Authorized transfer (rule 17) ──

    [Fact]
    public async Task AuthorizeTransfer_ThenNewInstallationActivates_Succeeds()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);
        await svc.ActivateAsync(license.LicenseKey, "INS-OLD-PC", null, "1.1.1.1");

        // Without authorization, the new PC is rejected exactly like the anti-resale case.
        var blocked = await svc.ActivateAsync(license.LicenseKey, "INS-NEW-PC", null, "2.2.2.2");
        Assert.False(blocked.Success);

        var transferred = await svc.AuthorizeTransferAsync(license.Id, "INS-OLD-PC", AdminId, "Customer replaced their PC");
        Assert.True(transferred);

        var afterTransfer = await svc.ActivateAsync(license.LicenseKey, "INS-NEW-PC", null, "2.2.2.2");
        Assert.True(afterTransfer.Success);

        var detail = await svc.GetDetailAsync(license.Id);
        Assert.Equal(1, detail!.ActiveInstallationCount);
        Assert.Contains(detail.Installations, i => i.InstallationId == "INS-NEW-PC" && i.Status == "active");
        Assert.Contains(detail.Installations, i => i.InstallationId == "INS-OLD-PC" && i.Status == "deactivated");
        Assert.Equal(1, detail.TransferCount);
    }

    [Fact]
    public async Task AuthorizeTransfer_RequiresReason()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);
        await svc.ActivateAsync(license.LicenseKey, "INS-0001", null, "1.1.1.1");

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AuthorizeTransferAsync(license.Id, "INS-0001", AdminId, ""));
    }

    [Fact]
    public async Task RecordLifecycleEvent_IsIdempotent_AndRejectsOtherActiveInstallation()
    {
        var (svc, db, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);
        await svc.ActivateAsync(license.LicenseKey, "INS-0001", null, "1.1.1.1");
        var operationId = Guid.NewGuid();
        var request = new RecordLocalLifecycleEventRequest
        {
            LicenseKey = license.LicenseKey,
            InstallationId = "INS-0001",
            OperationId = operationId,
            EventType = LocalLifecycleEventTypes.NewGymStarted,
            GymCode = "GYM-OLD-0001",
            GymName = "Old Gym",
            Message = "replace",
        };

        var first = await svc.RecordLifecycleEventAsync(request);
        var second = await svc.RecordLifecycleEventAsync(request);

        Assert.Equal(first.CreatedAtUtc, second.CreatedAtUtc);
        Assert.Equal(1, await db.LocalLifecycleEvents.CountAsync());
        var detail = await svc.GetDetailAsync(license.Id);
        Assert.Single(detail!.RecentOperations);
        Assert.Equal("GYM-OLD-0001", detail.Installations.Single(i => i.InstallationId == "INS-0001").GymCode);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordLifecycleEventAsync(new RecordLocalLifecycleEventRequest
        {
            LicenseKey = license.LicenseKey,
            InstallationId = "INS-SPOOF",
            OperationId = Guid.NewGuid(),
            EventType = LocalLifecycleEventTypes.SetupCompleted,
        }));

        var listed = (await svc.ListAsync()).Single(l => l.Id == license.Id);
        Assert.Equal("GYM-OLD-0001", listed.GymCode);
        Assert.Equal("Old Gym", listed.GymName);
        Assert.Equal("active", listed.InstallationStatus);
    }

    [Fact]
    public async Task ListAsync_DoesNotChangeGymIdentity_WhenCheckInIsStale()
    {
        var (svc, db, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);
        await svc.ActivateAsync(license.LicenseKey, "INS-0001", null, "1.1.1.1");
        await svc.RecordLifecycleEventAsync(new RecordLocalLifecycleEventRequest
        {
            LicenseKey = license.LicenseKey,
            InstallationId = "INS-0001",
            OperationId = Guid.NewGuid(),
            EventType = LocalLifecycleEventTypes.SetupCompleted,
            GymCode = "GYM-LIVE-0001",
            GymName = "Live Gym",
            AppVersion = "1.4.0",
        });

        var install = await db.LocalInstallations.SingleAsync(i => i.InstallationId == "INS-0001");
        install.LastValidatedAtUtc = DateTime.UtcNow.AddDays(-40);
        await db.SaveChangesAsync();

        var listed = (await svc.ListAsync()).Single(l => l.Id == license.Id);
        Assert.Equal("GYM-LIVE-0001", listed.GymCode);
        Assert.Equal("Live Gym", listed.GymName);
        Assert.Equal("1.4.0", listed.AppVersion);
        Assert.Equal(LocalLicenseStatuses.Active, listed.Status);
    }

    [Fact]
    public async Task ValidateAsync_PersistsGymIdentity_WithoutChangingLicenseStatus()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);
        await svc.ActivateAsync(license.LicenseKey, "INS-0001", null, "1.1.1.1");

        var first = await svc.ValidateAsync(
            license.LicenseKey, "INS-0001", "1.1.1.1",
            gymCode: "GYM-GYM-4191", gymName: "Gold", appVersion: "3.0.0");
        Assert.True(first.Success);
        Assert.Equal(LocalActivationResults.ValidationSuccess, first.Result);

        var afterReport = await svc.GetDetailAsync(license.Id);
        Assert.Equal(LocalLicenseStatuses.Active, afterReport!.Status);
        Assert.Equal("GYM-GYM-4191", afterReport.GymCode);
        Assert.Equal("Gold", afterReport.GymName);
        Assert.Equal("3.0.0", afterReport.AppVersion);

        var keep = await svc.ValidateAsync(license.LicenseKey, "INS-0001", "1.1.1.1");
        Assert.True(keep.Success);
        var afterKeep = await svc.GetDetailAsync(license.Id);
        Assert.Equal("GYM-GYM-4191", afterKeep!.GymCode);
        Assert.Equal("Gold", afterKeep.GymName);
        Assert.Equal("3.0.0", afterKeep.AppVersion);
        Assert.Equal(LocalLicenseStatuses.Active, afterKeep.Status);
    }

    [Fact]
    public async Task RecordLifecycleEvent_UnknownType_IsRejected()
    {
        var (svc, _, _, customerId) = NewSut();
        var license = await svc.IssueAsync(IssueReq(customerId), AdminId);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordLifecycleEventAsync(new RecordLocalLifecycleEventRequest
        {
            LicenseKey = license.LicenseKey,
            InstallationId = "INS-0001",
            OperationId = Guid.NewGuid(),
            EventType = "NotARealEvent",
        }));
    }

    // ── Product/edition guard (rule 12/16 - "wrong product rejected") ──

    [Fact]
    public async Task ActivateAsync_WrongProduct_Rejected()
    {
        var db = NewDb();
        var repo = new LocalLicenseWriteRepository(db);
        var signer = NewSigner(out _);
        var svc = new LocalLicenseService(repo, signer);

        // A license for a hypothetical different product, constructed directly (IssueAsync always
        // sets Product="HyMotion" - this simulates data that should never activate against the
        // HyMotion Local client).
        var license = new LocalLicense
        {
            LicenseKey = "HY-LCL-OTHER-00001",
            CustomerName = "Some Customer",
            Product = "SomeOtherProduct",
            Edition = "Lifetime",
            DeviceLimit = 1,
            Status = LocalLicenseStatuses.PendingActivation,
        };
        db.LocalLicenses.Add(license);
        await db.SaveChangesAsync();

        var result = await svc.ActivateAsync(license.LicenseKey, "INS-0001", null, "1.1.1.1");

        Assert.False(result.Success);
        Assert.Equal(LocalActivationResults.WrongProduct, result.Result);
    }
}
