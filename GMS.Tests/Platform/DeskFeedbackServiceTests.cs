namespace GMS.Tests.Platform;

using Microsoft.EntityFrameworkCore;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

public class DeskFeedbackServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Sender = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Reviewer = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static (DeskFeedbackService svc, PlatformDbContext db) NewSut()
    {
        var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase("desk-fb-" + Guid.NewGuid())
            .Options);
        var svc = new DeskFeedbackService(db, new NoOpAudit());
        return (svc, db);
    }

    private sealed class NoOpAudit : IPlatformAuditService
    {
        public Task LogAsync(Guid actorPlatformUserId, string action, Guid? tenantId = null, object? before = null, object? after = null, string? ipAddress = null)
            => Task.CompletedTask;

        public Task<PlatformPagedResult<PlatformAuditLogDto>> ListAsync(Guid? tenantId, string? action, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformPagedResult<PlatformAuditLogDto> { Items = [], TotalCount = 0, Page = page, PageSize = pageSize });
    }

    private static DeskFeedbackActorContext Actor(Guid tenantId, Guid? sender = null) => new()
    {
        TenantId = tenantId,
        GymCode = "GOLD",
        GymName = "Gold Gym",
        SenderUserId = sender ?? Sender,
        SenderRole = "Owner",
        SenderEmail = "owner@gym.test",
        SenderDisplayName = "Owner",
    };

    [Fact]
    public async Task Submit_PersistsWithDerivedTenantAndLinksCustomerByTenantId()
    {
        var (svc, db) = NewSut();
        var customer = new PlatformCustomer
        {
            BusinessName = "Gold",
            OwnerName = "Ahmed",
            TenantId = TenantA,
            Status = PlatformCustomerStatuses.Active,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var created = await svc.SubmitAsync(new SubmitDeskFeedbackRequest
        {
            Category = DeskFeedbackCategories.Feature,
            Subject = "Batch print",
            Message = "Please add clearer batch preview options for PVC cards.",
            AppVersion = "1.0.0-beta",
            ClientRequestId = "req-1",
        }, Actor(TenantA));

        Assert.False(created.AlreadySubmitted);
        Assert.Equal(customer.Id, created.CustomerId);
        Assert.Equal(TenantA, created.TenantId);
        Assert.Equal(DeskFeedbackStatuses.New, created.Status);
        Assert.Equal(DeskFeedbackCategories.Feature, created.Category);
        Assert.Equal(1, await db.DeskFeedback.CountAsync());
    }

    [Fact]
    public async Task Submit_RejectsShortMessageAndUnknownCategory()
    {
        var (svc, _) = NewSut();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SubmitAsync(
            new SubmitDeskFeedbackRequest { Category = DeskFeedbackCategories.General, Message = "too short" },
            Actor(TenantA)));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SubmitAsync(
            new SubmitDeskFeedbackRequest { Category = "roadmap", Message = "This is long enough for validation." },
            Actor(TenantA)));
    }

    [Fact]
    public async Task Submit_IdempotentOnClientRequestId_AndNearDuplicate()
    {
        var (svc, db) = NewSut();
        var body = new SubmitDeskFeedbackRequest
        {
            Category = DeskFeedbackCategories.Problem,
            Message = "Access cards table was white in dark mode and hard to read.",
            ClientRequestId = "same-key",
        };
        var first = await svc.SubmitAsync(body, Actor(TenantA));
        var second = await svc.SubmitAsync(body, Actor(TenantA));
        Assert.Equal(first.Id, second.Id);
        Assert.True(second.AlreadySubmitted);
        Assert.Equal(1, await db.DeskFeedback.CountAsync());

        var nearDup = await svc.SubmitAsync(new SubmitDeskFeedbackRequest
        {
            Category = DeskFeedbackCategories.Problem,
            Message = "Access cards table was white in dark mode and hard to read.",
            ClientRequestId = "other-key",
        }, Actor(TenantA));
        Assert.True(nearDup.AlreadySubmitted);
        Assert.Equal(first.Id, nearDup.Id);
    }

    [Fact]
    public async Task List_DoesNotLeakOtherTenantRows_WhenFilteredByTenant()
    {
        var (svc, _) = NewSut();
        await svc.SubmitAsync(new SubmitDeskFeedbackRequest
        {
            Category = DeskFeedbackCategories.General,
            Message = "Feedback from tenant A that should stay isolated.",
        }, Actor(TenantA));
        await svc.SubmitAsync(new SubmitDeskFeedbackRequest
        {
            Category = DeskFeedbackCategories.General,
            Message = "Feedback from tenant B that should stay isolated.",
        }, Actor(TenantB));

        var onlyA = await svc.ListAsync(null, TenantA, null, null, null, null);
        Assert.Single(onlyA);
        Assert.Equal(TenantA, onlyA[0].TenantId);
    }

    [Fact]
    public async Task Update_StatusAndNotes_ForSupportReview()
    {
        var (svc, _) = NewSut();
        var created = await svc.SubmitAsync(new SubmitDeskFeedbackRequest
        {
            Category = DeskFeedbackCategories.Feature,
            Message = "Add member app freeze from the mobile profile screen please.",
        }, Actor(TenantA));

        var updated = await svc.UpdateAsync(created.Id, new UpdateDeskFeedbackRequest
        {
            Status = DeskFeedbackStatuses.UnderReview,
            InternalNote = "Product will triage next sprint.",
            ResponseToCustomer = null,
        }, Reviewer);

        Assert.NotNull(updated);
        Assert.Equal(DeskFeedbackStatuses.UnderReview, updated!.Status);
        Assert.Equal("Product will triage next sprint.", updated.InternalNote);
        Assert.Equal(Reviewer, updated.ReviewedByPlatformAdminUserId);
    }

    [Fact]
    public async Task SubmitFromLocalLicense_LinksCustomerFromLicense()
    {
        var (svc, db) = NewSut();
        var customer = new PlatformCustomer { BusinessName = "Local Gym Co", OwnerName = "Owner" };
        db.Customers.Add(customer);
        var license = new LocalLicense
        {
            LicenseKey = "HY-LCL-TEST-FEED",
            CustomerName = "Local Gym Co",
            CustomerId = customer.Id,
            Status = LocalLicenseStatuses.Active,
        };
        db.LocalLicenses.Add(license);
        db.LocalInstallations.Add(new LocalInstallation
        {
            LicenseId = license.Id,
            InstallationId = "INS-FEEDTEST1",
            Status = LocalInstallationStatuses.Active,
            GymCode = "GYM-4191",
            GymName = "Test Gym",
        });
        await db.SaveChangesAsync();

        var created = await svc.SubmitFromLocalLicenseAsync(new IngestLocalDeskFeedbackRequest
        {
            LicenseKey = "HY-LCL-TEST-FEED",
            InstallationId = "INS-FEEDTEST1",
            TenantId = TenantA,
            SenderUserId = Sender,
            SenderRole = "Owner",
            GymCode = "GYM-4191",
            GymName = "Test Gym",
            Category = DeskFeedbackCategories.Feature,
            Message = "Please add dark mode polish for the plans validity modal.",
            ClientRequestId = "local-forward-1",
        });

        Assert.Equal(customer.Id, created.CustomerId);
        Assert.Equal("Local Gym Co", created.CustomerName);
        Assert.Equal("GYM-4191", created.GymCode);
        // Body TenantId is ignored — Local-only licenses partition by license Id.
        Assert.Equal(license.Id, created.TenantId);
        Assert.Equal("Please add dark mode polish for the plans validity modal.", created.Message);
    }

    [Fact]
    public async Task SubmitFromLocalLicense_IgnoresSpoofedTenantId_AndUsesCloudTenantWhenLinked()
    {
        var (svc, db) = NewSut();
        var customer = new PlatformCustomer
        {
            BusinessName = "Linked Gym",
            OwnerName = "Owner",
            TenantId = TenantA,
        };
        db.Customers.Add(customer);
        var license = new LocalLicense
        {
            LicenseKey = "HY-LCL-TEST-SPOOF",
            CustomerName = "Linked Gym",
            CustomerId = customer.Id,
            Status = LocalLicenseStatuses.Active,
        };
        db.LocalLicenses.Add(license);
        db.LocalInstallations.Add(new LocalInstallation
        {
            LicenseId = license.Id,
            InstallationId = "INS-SPOOF1",
            Status = LocalInstallationStatuses.Active,
            GymCode = "GYM-SPOOF",
        });
        await db.SaveChangesAsync();

        var created = await svc.SubmitFromLocalLicenseAsync(new IngestLocalDeskFeedbackRequest
        {
            LicenseKey = "HY-LCL-TEST-SPOOF",
            InstallationId = "INS-SPOOF1",
            TenantId = TenantB, // spoofed — must be ignored
            SenderUserId = Sender,
            SenderRole = "Owner",
            Category = DeskFeedbackCategories.Problem,
            Subject = "Title only?",
            Message = "Full message body must persist even when a subject is present.",
            ClientRequestId = "spoof-1",
        });

        Assert.Equal(customer.Id, created.CustomerId);
        Assert.Equal(TenantA, created.TenantId);
        Assert.Equal("Title only?", created.Subject);
        Assert.Equal("Full message body must persist even when a subject is present.", created.Message);
    }
}
