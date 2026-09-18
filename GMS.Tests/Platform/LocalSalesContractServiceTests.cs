namespace GMS.Tests.Platform;

using Microsoft.EntityFrameworkCore;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

public class LocalSalesContractServiceTests
{
    private static readonly Guid Actor = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static (LocalSalesContractService docs, PlatformCustomerService commerce, PlatformDbContext db, LocalLicenseService licenses)
        NewSut()
    {
        var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase("lsc-" + Guid.NewGuid())
            .Options);
        db.PlatformAdminUsers.Add(new PlatformAdminUser
        {
            Id = Actor,
            Email = "ops@gymflow.local",
            PasswordHash = "x",
            FullName = "Ops",
            Role = PlatformRoles.Ops,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        });
        db.SaveChanges();
        var audit = new NoOpAudit();
        var licenses = new LocalLicenseService(new LocalLicenseWriteRepository(db), new NullSigner(), db);
        var commerce = new PlatformCustomerService(db, audit, licenses);
        var docs = new LocalSalesContractService(db, audit);
        return (docs, commerce, db, licenses);
    }

    private sealed class NullSigner : ILicenseSigningService
    {
        public string Sign(GMS.Core.Licensing.LocalLicensePayload payload) => "test-signature";
        public string Sign(byte[] data) => "test-signature";
    }

    private sealed class NoOpAudit : IPlatformAuditService
    {
        public Task LogAsync(Guid actorPlatformUserId, string action, Guid? tenantId = null, object? before = null, object? after = null, string? ipAddress = null)
            => Task.CompletedTask;

        public Task<PlatformPagedResult<PlatformAuditLogDto>> ListAsync(Guid? tenantId, string? action, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformPagedResult<PlatformAuditLogDto> { Items = [], TotalCount = 0, Page = page, PageSize = pageSize });
    }

    private static async Task<(PlatformCustomerDetailDto customer, PlatformContractDto contract)> SeedSale(
        PlatformCustomerService commerce,
        PlatformDbContext db,
        decimal price = 15000m,
        decimal headerDiscount = 0m)
    {
        var product = new PlatformCatalogProduct
        {
            Sku = "HY-SW-LOCAL-LT",
            Name = "HyMotion Local Lifetime",
            ProductType = PlatformCatalogProductTypes.Software,
            DefaultPrice = price,
        };
        db.CatalogProducts.Add(product);
        await db.SaveChangesAsync();
        var customer = await commerce.CreateCustomerAsync(new UpsertPlatformCustomerRequest
        {
            BusinessName = "Nile Strength Gym",
            OwnerName = "Karim El-Sayed",
            Phone = "01044557788",
            Email = "karim@nilestrength.example",
            Address = "Cairo",
        }, Actor);
        var contract = await commerce.CreateContractAsync(new CreatePlatformContractRequest
        {
            CustomerId = customer.Id,
            Discount = headerDiscount,
            Items = [new ContractItemInput { CatalogProductId = product.Id, Quantity = 1, UnitPrice = price }],
        }, Actor);
        return (customer, contract);
    }

    [Fact]
    public async Task Preview_Unpaid_OutstandingIsNotZero()
    {
        var (docs, commerce, db, _) = NewSut();
        var (_, contract) = await SeedSale(commerce, db);
        var preview = await docs.PreviewAsync(contract.Id, "en");
        Assert.False(preview.Issued);
        Assert.Contains("dir=\"ltr\"", preview.Html);
        Assert.Contains("unpaid", preview.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Outstanding at issue", preview.Html);
        Assert.Contains("15,000 EGP", preview.Html);
        Assert.DoesNotContain("This is not a fully paid sale", preview.Html);
    }

    [Fact]
    public async Task Preview_Arabic_UsesRtl()
    {
        var (docs, commerce, db, _) = NewSut();
        var (_, contract) = await SeedSale(commerce, db);
        var preview = await docs.PreviewAsync(contract.Id, "ar");
        Assert.Contains("dir=\"rtl\"", preview.Html);
        Assert.Contains("lang=\"ar\"", preview.Html);
        Assert.Contains("المتبقي وقت الإصدار", preview.Html);
    }

    [Fact]
    public async Task Issue_ThenLaterPayment_DoesNotChangeIssuedPaper()
    {
        var (docs, commerce, db, _) = NewSut();
        var (customer, contract) = await SeedSale(commerce, db);
        await commerce.RecordPaymentAsync(new RecordCustomerPaymentRequest
        {
            ContractId = contract.Id,
            Amount = 5000m,
            PaymentMethod = PlatformPaymentMethods.Cash,
        }, Actor);

        var issued = await docs.IssueAsync(contract.Id, "en", Actor);
        Assert.True(issued.Issued);
        Assert.Contains("5,000 EGP", issued.Html);
        Assert.Contains("10,000 EGP", issued.Html);
        Assert.DoesNotContain(customer.BusinessName + "CHANGED", issued.Html);

        await commerce.RecordPaymentAsync(new RecordCustomerPaymentRequest
        {
            ContractId = contract.Id,
            Amount = 5000m,
            PaymentMethod = PlatformPaymentMethods.BankTransfer,
        }, Actor);
        var live = await commerce.GetContractAsync(contract.Id);
        Assert.Equal(10000m, live!.PaidAmount);
        Assert.Equal(5000m, live.OutstandingAmount);

        var paper = await docs.GetIssuedHtmlAsync(contract.Id);
        Assert.Contains("5,000 EGP", paper!.Html);
        Assert.Contains("10,000 EGP", paper.Html);
        Assert.Contains("This is not a fully paid sale", paper.Html);
        Assert.Equal(0, paper.PrintCount);
    }

    [Fact]
    public async Task DuplicateIssue_IsRejected()
    {
        var (docs, commerce, db, _) = NewSut();
        var (_, contract) = await SeedSale(commerce, db);
        await docs.IssueAsync(contract.Id, "en", Actor);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => docs.IssueAsync(contract.Id, "en", Actor));
        Assert.Contains("Reprint", ex.Message);
    }

    [Fact]
    public async Task Reprint_IncrementsCount_WithoutRecalculatingMoney()
    {
        var (docs, commerce, db, _) = NewSut();
        var (_, contract) = await SeedSale(commerce, db);
        await commerce.RecordPaymentAsync(new RecordCustomerPaymentRequest
        {
            ContractId = contract.Id,
            Amount = 5000m,
            PaymentMethod = PlatformPaymentMethods.Cash,
        }, Actor);
        await docs.IssueAsync(contract.Id, "en", Actor);
        await commerce.RecordPaymentAsync(new RecordCustomerPaymentRequest
        {
            ContractId = contract.Id,
            Amount = 10000m,
            PaymentMethod = PlatformPaymentMethods.Cash,
        }, Actor);

        var reprint = await docs.ReprintAsync(contract.Id, Actor);
        Assert.Equal(1, reprint.PrintCount);
        Assert.Contains("5,000 EGP", reprint.Html);
        Assert.Contains("10,000 EGP", reprint.Html);
        Assert.Contains("partial", reprint.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("The live sale was fully paid when this contract was issued.", reprint.Html);
    }

    [Fact]
    public async Task TermsChange_DoesNotRewriteIssuedPaper()
    {
        var (docs, commerce, db, _) = NewSut();
        var (_, contract) = await SeedSale(commerce, db);
        var issued = await docs.IssueAsync(contract.Id, "en", Actor);
        Assert.Contains("Lifetime means the license has no expiry date", issued.Html);

        await docs.UpdateTermsAsync(new UpsertLocalSalesContractTermsRequest
        {
            TermsEn = "NEW TERMS MUST NOT APPEAR ON OLD PAPER",
            TermsAr = "شروط جديدة",
        }, Actor);

        var paper = await docs.GetIssuedHtmlAsync(contract.Id);
        Assert.DoesNotContain("NEW TERMS MUST NOT APPEAR ON OLD PAPER", paper!.Html);
        Assert.Contains("Lifetime means the license has no expiry date", paper.Html);

        var preview = await docs.PreviewAsync(contract.Id, "en");
        Assert.Contains("NEW TERMS MUST NOT APPEAR ON OLD PAPER", preview.Html);
    }

    [Fact]
    public async Task CustomerRename_DoesNotRewriteIssuedPaper()
    {
        var (docs, commerce, db, _) = NewSut();
        var (customer, contract) = await SeedSale(commerce, db);
        await docs.IssueAsync(contract.Id, "en", Actor);
        await commerce.UpdateCustomerAsync(customer.Id, new UpsertPlatformCustomerRequest
        {
            BusinessName = "Renamed Gym LLC",
            OwnerName = customer.OwnerName,
            Phone = customer.Phone,
            Email = customer.Email,
            Address = customer.Address,
        }, Actor);
        var paper = await docs.GetIssuedHtmlAsync(contract.Id);
        Assert.Contains("Nile Strength Gym", paper!.Html);
        Assert.DoesNotContain("Renamed Gym LLC", paper.Html);
    }

    [Fact]
    public async Task CancelledSale_CannotIssuePaper()
    {
        var (docs, commerce, db, _) = NewSut();
        var (_, contract) = await SeedSale(commerce, db);
        await commerce.ChangeContractStatusAsync(contract.Id, PlatformContractStatuses.Cancelled, Actor);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => docs.IssueAsync(contract.Id, "en", Actor));
        Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IssuedHtml_MasksLicenseKey()
    {
        var (docs, commerce, db, licenses) = NewSut();
        var (customer, contract) = await SeedSale(commerce, db);
        var license = await licenses.IssueAsync(new IssueLocalLicenseRequest
        {
            CustomerId = customer.Id,
            ContractId = contract.Id,
            DeviceLimit = 1,
        }, Actor);
        var paper = await docs.IssueAsync(contract.Id, "en", Actor);
        Assert.DoesNotContain(license.LicenseKey, paper.Html);
        Assert.Contains("•••••", paper.Html);
        Assert.Contains("HY-LCL-", paper.Html);
        Assert.Contains("Lifetime", paper.Html);
        Assert.Contains("Allowed computers", paper.Html);
    }

    [Fact]
    public void MaskLicenseKey_HidesLastSegment()
    {
        Assert.Equal("HY-LCL-8F92A-•••••", LocalSalesContractHtmlBuilder.MaskLicenseKey("HY-LCL-8F92A-A81D1"));
    }
}
