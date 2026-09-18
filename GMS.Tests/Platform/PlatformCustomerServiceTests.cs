namespace GMS.Tests.Platform;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

public class PlatformCustomerServiceTests
{
    private static readonly Guid Actor = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static (PlatformCustomerService svc, PlatformDbContext db, LocalLicenseService licenses) NewSut()
    {
        var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase("cust-" + Guid.NewGuid())
            .Options);
        SeedPlatformUser(db, Actor, isActive: true);
        var licenses = new LocalLicenseService(new LocalLicenseWriteRepository(db), new NullSigner(), db);
        var svc = new PlatformCustomerService(db, new NoOpAudit(), licenses);
        return (svc, db, licenses);
    }

    private static void SeedPlatformUser(PlatformDbContext db, Guid id, bool isActive)
    {
        db.PlatformAdminUsers.Add(new PlatformAdminUser
        {
            Id = id,
            Email = $"{id:N}@gymflow.local",
            PasswordHash = "x",
            FullName = "Test Operator",
            Role = PlatformRoles.Support,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow,
        });
        db.SaveChanges();
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

    private static async Task<PlatformCatalogProduct> SeedProduct(PlatformDbContext db, string sku, string name, decimal price)
    {
        var p = new PlatformCatalogProduct { Sku = sku, Name = name, ProductType = PlatformCatalogProductTypes.Software, DefaultPrice = price };
        db.CatalogProducts.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    [Fact]
    public async Task CreateAndUpdateCustomer_Works()
    {
        var (svc, _, _) = NewSut();
        var created = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest
        {
            BusinessName = "Fitness Gym",
            OwnerName = "Ahmed",
            Phone = "01000000000",
            Email = "owner@gym.test",
            LeadSource = "Instagram",
        }, Actor);

        Assert.Equal("Fitness Gym", created.BusinessName);
        Assert.Equal(PlatformCustomerStatuses.Prospect, created.Status);
        Assert.Equal("Instagram", created.LeadSource);
        Assert.Equal(Actor, created.CreatedByPlatformAdminUserId);

        var updated = await svc.UpdateCustomerAsync(created.Id, new UpsertPlatformCustomerRequest
        {
            BusinessName = "Fitness Gym",
            OwnerName = "Ahmed",
            Status = PlatformCustomerStatuses.Active,
            LeadSource = "Instagram",
        }, Actor);
        Assert.Equal(PlatformCustomerStatuses.Active, updated!.Status);
    }

    [Fact]
    public async Task ContractItems_SnapshotSurvivesProductRename()
    {
        var (svc, db, _) = NewSut();
        var product = await SeedProduct(db, "HY-SW-LOCAL-LT", "HyMotion Local Lifetime", 25000m);
        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Fitness Gym", OwnerName = "Ahmed" }, Actor);
        var contract = await svc.CreateContractAsync(new CreatePlatformContractRequest
        {
            CustomerId = customer.Id,
            Items =
            [
                new ContractItemInput { CatalogProductId = product.Id, Quantity = 1 },
                new ContractItemInput { CatalogProductId = product.Id, Quantity = 1, DiscountAmount = 1000 },
            ],
        }, Actor);

        Assert.Equal(50000m, contract.Subtotal);
        Assert.Equal(49000m, contract.Total);
        Assert.Equal("HyMotion Local Lifetime", contract.Items[0].NameSnapshot);

        product.Name = "Renamed Local";
        await db.SaveChangesAsync();
        var again = await svc.GetContractAsync(contract.Id);
        Assert.Equal("HyMotion Local Lifetime", again!.Items[0].NameSnapshot);
    }

    [Fact]
    public async Task Payments_PartialThenFull_UpdateOutstanding()
    {
        var (svc, db, _) = NewSut();
        var product = await SeedProduct(db, "HY-SVC-INSTALL", "Installation", 1500m);
        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Fitness Gym", OwnerName = "Ahmed" }, Actor);
        var contract = await svc.CreateContractAsync(new CreatePlatformContractRequest
        {
            CustomerId = customer.Id,
            Items = [new ContractItemInput { CatalogProductId = product.Id, Quantity = 1 }],
        }, Actor);
        Assert.Equal("unpaid", contract.PaymentStatus);

        var p1 = await svc.RecordPaymentAsync(new RecordCustomerPaymentRequest
        {
            ContractId = contract.Id,
            Amount = 500m,
            PaymentMethod = PlatformPaymentMethods.Cash,
        }, Actor);
        Assert.Equal(500m, p1.Amount);
        var afterPartial = await svc.GetContractAsync(contract.Id);
        Assert.Equal("partial", afterPartial!.PaymentStatus);
        Assert.Equal(1000m, afterPartial.OutstandingAmount);

        await svc.RecordPaymentAsync(new RecordCustomerPaymentRequest
        {
            ContractId = contract.Id,
            Amount = 1000m,
            PaymentMethod = PlatformPaymentMethods.BankTransfer,
        }, Actor);
        var afterFull = await svc.GetContractAsync(contract.Id);
        Assert.Equal("paid", afterFull!.PaymentStatus);
        Assert.Equal(0m, afterFull.OutstandingAmount);
    }

    [Fact]
    public async Task ContractStatus_RejectsIllegalTransition()
    {
        var (svc, db, _) = NewSut();
        var product = await SeedProduct(db, "HY-HW-PRINTER", "Receipt Printer", 2200m);
        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Fitness Gym", OwnerName = "Ahmed" }, Actor);
        var contract = await svc.CreateContractAsync(new CreatePlatformContractRequest
        {
            CustomerId = customer.Id,
            Items = [new ContractItemInput { CatalogProductId = product.Id, Quantity = 1 }],
        }, Actor);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ChangeContractStatusAsync(contract.Id, PlatformContractStatuses.Active, Actor));

        var pending = await svc.ChangeContractStatusAsync(contract.Id, PlatformContractStatuses.Pending, Actor);
        Assert.Equal("pending", pending!.Status);
        var active = await svc.ChangeContractStatusAsync(contract.Id, PlatformContractStatuses.Active, Actor);
        Assert.Equal("active", active!.Status);
    }

    [Fact]
    public async Task License_AssociatesWithCustomerAndContract()
    {
        var (svc, db, licenses) = NewSut();
        var product = await SeedProduct(db, "HY-SW-LOCAL-LT", "HyMotion Local Lifetime", 25000m);
        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Fitness Gym", OwnerName = "Ahmed" }, Actor);
        var contract = await svc.CreateContractAsync(new CreatePlatformContractRequest
        {
            CustomerId = customer.Id,
            Items = [new ContractItemInput { CatalogProductId = product.Id, Quantity = 1 }],
        }, Actor);

        var license = await licenses.IssueAsync(new IssueLocalLicenseRequest
        {
            CustomerId = customer.Id,
            ContractId = contract.Id,
            DeviceLimit = 1,
        }, Actor);

        Assert.Equal(customer.Id, license.CustomerId);
        Assert.Equal(contract.Id, license.ContractId);
        Assert.Equal("Fitness Gym", license.CustomerName);
        Assert.StartsWith("HY-LCL-", license.LicenseKey);

        var profile = await svc.GetProfileAsync(customer.Id);
        Assert.Equal(license.Id, profile!.License!.Id);
        Assert.Contains(profile.PurchasedItems, i => i.NameSnapshot == "HyMotion Local Lifetime");
    }

    [Fact]
    public async Task SupportTicket_LinksCustomerLicenseInstallation()
    {
        var (svc, db, licenses) = NewSut();
        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Fitness Gym", OwnerName = "Ahmed" }, Actor);
        var license = await licenses.IssueAsync(new IssueLocalLicenseRequest { CustomerId = customer.Id, DeviceLimit = 1 }, Actor);
        await licenses.ActivateAsync(license.LicenseKey, "INST-001", null, "127.0.0.1");
        await licenses.RecordLifecycleEventAsync(new RecordLocalLifecycleEventRequest
        {
            LicenseKey = license.LicenseKey,
            InstallationId = "INST-001",
            OperationId = Guid.NewGuid(),
            EventType = LocalLifecycleEventTypes.SetupCompleted,
            GymCode = "GYM-TEST-0001",
            GymName = "Fitness Gym",
        });
        var detail = await licenses.GetDetailAsync(license.Id);

        var ticket = await svc.CreateTicketAsync(new CreateSupportTicketRequest
        {
            CustomerId = customer.Id,
            LocalLicenseId = license.Id,
            LocalInstallationId = detail!.Installations[0].Id,
            Subject = "Reception printer not working",
            Description = "Printer at front desk does not print receipts.",
            Priority = PlatformSupportTicketPriorities.High,
        }, Actor);

        Assert.StartsWith("HY-SUP-", ticket.TicketNumber);
        Assert.Equal("open", ticket.Status);
        Assert.Equal(license.Id, ticket.LocalLicenseId);

        var updated = await svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest
        {
            Status = PlatformSupportTicketStatuses.InProgress,
            AssignedToPlatformAdminUserId = Actor,
        }, Actor);
        Assert.Equal("in_progress", updated!.Status);
        Assert.Equal(Actor, updated.AssignedToPlatformAdminUserId);

        var profile = await svc.GetProfileAsync(customer.Id);
        Assert.Equal(1, profile!.OpenSupportTicketCount);
        Assert.Equal(detail.Installations[0].Id, ticket.LocalInstallationId);
        Assert.Equal("GYM-TEST-0001", profile.Licenses.Single().GymCode);
        Assert.Equal("Fitness Gym", profile.Licenses.Single().GymName);
    }

    [Fact]
    public async Task CreateTicket_RejectsLicenseAndInstallationFromOtherCustomer()
    {
        var (svc, _, licenses) = NewSut();
        var customerA = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Gym A", OwnerName = "A" }, Actor);
        var customerB = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Gym B", OwnerName = "B" }, Actor);
        var licenseA = await licenses.IssueAsync(new IssueLocalLicenseRequest { CustomerId = customerA.Id, DeviceLimit = 1 }, Actor);
        var licenseB = await licenses.IssueAsync(new IssueLocalLicenseRequest { CustomerId = customerB.Id, DeviceLimit = 1 }, Actor);
        await licenses.ActivateAsync(licenseB.LicenseKey, "INST-B", null, "127.0.0.1");
        var detailB = await licenses.GetDetailAsync(licenseB.Id);

        var licenseEx = await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateTicketAsync(new CreateSupportTicketRequest
        {
            CustomerId = customerA.Id,
            LocalLicenseId = licenseB.Id,
            Subject = "Wrong gym",
            Description = "Must not attach the other customer's license.",
        }, Actor));
        Assert.Equal("License does not belong to this customer.", licenseEx.Message);

        var installEx = await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateTicketAsync(new CreateSupportTicketRequest
        {
            CustomerId = customerA.Id,
            LocalLicenseId = licenseA.Id,
            LocalInstallationId = detailB!.Installations[0].Id,
            Subject = "Wrong install",
            Description = "Must not attach the other customer's installation.",
        }, Actor));
        Assert.Equal("Installation does not belong to the selected license.", installEx.Message);

        var orphanInstallEx = await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateTicketAsync(new CreateSupportTicketRequest
        {
            CustomerId = customerA.Id,
            LocalInstallationId = detailB.Installations[0].Id,
            Subject = "Orphan install",
            Description = "Installation-only spoof must still fail ownership.",
        }, Actor));
        Assert.Equal("Installation does not belong to this customer.", orphanInstallEx.Message);
    }

    [Fact]
    public async Task UpdateTicket_EnforcesTransitionGraph()
    {
        var (svc, _, _) = NewSut();
        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Fitness Gym", OwnerName = "Ahmed" }, Actor);
        var ticket = await svc.CreateTicketAsync(new CreateSupportTicketRequest
        {
            CustomerId = customer.Id,
            Subject = "Gate reader offline",
            Description = "Installation has not checked in.",
        }, Actor);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.Resolved }, Actor));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.Closed }, Actor));

        var started = await svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.InProgress }, Actor);
        Assert.Equal(PlatformSupportTicketStatuses.InProgress, started!.Status);

        var waiting = await svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.WaitingCustomer }, Actor);
        Assert.Equal(PlatformSupportTicketStatuses.WaitingCustomer, waiting!.Status);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.Resolved }, Actor));

        var resumed = await svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.InProgress }, Actor);
        Assert.Equal(PlatformSupportTicketStatuses.InProgress, resumed!.Status);

        var resolved = await svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.Resolved }, Actor);
        Assert.Equal(PlatformSupportTicketStatuses.Resolved, resolved!.Status);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.InProgress }, Actor));

        var closed = await svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.Closed }, Actor);
        Assert.Equal(PlatformSupportTicketStatuses.Closed, closed!.Status);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.Open }, Actor));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.InProgress }, Actor));
    }

    [Fact]
    public async Task UpdateTicket_Closed_RejectsAssignmentPriorityResolutionAndStatus()
    {
        var (svc, db, _) = NewSut();
        var otherId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        SeedPlatformUser(db, otherId, isActive: true);
        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Fitness Gym", OwnerName = "Ahmed" }, Actor);
        var ticket = await svc.CreateTicketAsync(new CreateSupportTicketRequest
        {
            CustomerId = customer.Id,
            Subject = "Gate reader offline",
            Description = "Installation has not checked in.",
        }, Actor);

        await svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest
        {
            Status = PlatformSupportTicketStatuses.InProgress,
            AssignedToPlatformAdminUserId = Actor,
            Priority = PlatformSupportTicketPriorities.High,
            Resolution = "Replaced the reader",
        }, Actor);
        await svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.Resolved }, Actor);
        var closed = await svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.Closed }, Actor);
        Assert.Equal(PlatformSupportTicketStatuses.Closed, closed!.Status);

        var assignEx = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { AssignedToPlatformAdminUserId = otherId }, Actor));
        Assert.Equal("This ticket is closed and cannot be changed.", assignEx.Message);

        var priorityEx = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Priority = PlatformSupportTicketPriorities.Critical }, Actor));
        Assert.Equal("This ticket is closed and cannot be changed.", priorityEx.Message);

        var resolutionEx = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Resolution = "Different note" }, Actor));
        Assert.Equal("This ticket is closed and cannot be changed.", resolutionEx.Message);

        var statusEx = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest { Status = PlatformSupportTicketStatuses.InProgress }, Actor));
        Assert.Equal("This ticket is closed and cannot be changed.", statusEx.Message);

        var sameValues = await svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest
        {
            Status = PlatformSupportTicketStatuses.Closed,
            AssignedToPlatformAdminUserId = Actor,
            Priority = PlatformSupportTicketPriorities.High,
            Resolution = "Replaced the reader",
        }, Actor);
        Assert.Equal(PlatformSupportTicketStatuses.Closed, sameValues!.Status);
        Assert.Equal(Actor, sameValues.AssignedToPlatformAdminUserId);
        Assert.Equal(PlatformSupportTicketPriorities.High, sameValues.Priority);
        Assert.Equal("Replaced the reader", sameValues.Resolution);

        var stored = await db.SupportTickets.AsNoTracking().FirstAsync(t => t.Id == ticket.Id);
        Assert.Equal(PlatformSupportTicketStatuses.Closed, stored.Status);
        Assert.Equal(Actor, stored.AssignedToPlatformAdminUserId);
        Assert.Equal(PlatformSupportTicketPriorities.High, stored.Priority);
        Assert.Equal("Replaced the reader", stored.Resolution);
    }

    [Fact]
    public async Task TicketStatusGraph_AllowsOnlyDocumentedEdges()
    {
        Assert.True(PlatformSupportTicketStatuses.CanTransition("open", "in_progress"));
        Assert.True(PlatformSupportTicketStatuses.CanTransition("in_progress", "waiting_customer"));
        Assert.True(PlatformSupportTicketStatuses.CanTransition("waiting_customer", "in_progress"));
        Assert.True(PlatformSupportTicketStatuses.CanTransition("in_progress", "resolved"));
        Assert.True(PlatformSupportTicketStatuses.CanTransition("resolved", "closed"));
        Assert.False(PlatformSupportTicketStatuses.CanTransition("open", "resolved"));
        Assert.False(PlatformSupportTicketStatuses.CanTransition("open", "closed"));
        Assert.False(PlatformSupportTicketStatuses.CanTransition("waiting_customer", "resolved"));
        Assert.False(PlatformSupportTicketStatuses.CanTransition("resolved", "in_progress"));
        Assert.False(PlatformSupportTicketStatuses.CanTransition("closed", "open"));
        Assert.False(PlatformSupportTicketStatuses.CanTransition("closed", "in_progress"));
    }

    [Fact]
    public async Task PasswordReset_DoesNotReturnOrStorePassword()
    {
        var (svc, db, _) = NewSut();
        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest
        {
            BusinessName = "Fitness Gym",
            OwnerName = "Ahmed",
            OwnerEmail = "owner@gym.test",
        }, Actor);

        var result = await svc.InitiateOwnerPasswordResetAsync(customer.Id, "Owner locked out of gym PC after staff change", Actor);
        Assert.True(result!.Initiated);
        Assert.DoesNotContain("password=", result.Message, StringComparison.OrdinalIgnoreCase);
        var json = System.Text.Json.JsonSerializer.Serialize(customer);
        Assert.DoesNotContain("\"password\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        var stored = await db.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id);
        Assert.Equal(PlatformOwnerAccountStatuses.ResetRequested, stored.OwnerAccountStatus);
        Assert.NotNull(stored.PasswordResetInitiatedAtUtc);
        Assert.Null(stored.GetType().GetProperty("Password"));
    }

    [Fact]
    public async Task EndToEnd_FitnessGym_ProfileShowsCommercialAndLicense()
    {
        var (svc, db, licenses) = NewSut();
        var lifetime = await SeedProduct(db, "HY-SW-LOCAL-LT", "HyMotion Local Lifetime", 25000m);
        var install = await SeedProduct(db, "HY-SVC-INSTALL", "Installation", 1500m);
        var pvc = await SeedProduct(db, "HY-HW-PVC", "PVC Cards", 800m);
        var printer = await SeedProduct(db, "HY-HW-PRINTER", "Receipt Printer", 2200m);

        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest
        {
            BusinessName = "Fitness Gym",
            OwnerName = "Ahmed",
            OwnerEmail = "ahmed@fitness.test",
            LeadSource = "Sales Team",
        }, Actor);

        var contract = await svc.CreateContractAsync(new CreatePlatformContractRequest
        {
            CustomerId = customer.Id,
            Items =
            [
                new ContractItemInput { CatalogProductId = lifetime.Id, Quantity = 1 },
                new ContractItemInput { CatalogProductId = install.Id, Quantity = 1 },
                new ContractItemInput { CatalogProductId = pvc.Id, Quantity = 1 },
                new ContractItemInput { CatalogProductId = printer.Id, Quantity = 1 },
            ],
        }, Actor);
        await svc.ChangeContractStatusAsync(contract.Id, PlatformContractStatuses.Pending, Actor);
        await svc.ChangeContractStatusAsync(contract.Id, PlatformContractStatuses.Active, Actor);

        Assert.Equal(29500m, contract.Total);
        await svc.RecordPaymentAsync(new RecordCustomerPaymentRequest
        {
            ContractId = contract.Id,
            Amount = 15000m,
            PaymentMethod = PlatformPaymentMethods.BankTransfer,
        }, Actor);

        var license = await licenses.IssueAsync(new IssueLocalLicenseRequest
        {
            CustomerId = customer.Id,
            ContractId = contract.Id,
            DeviceLimit = 1,
        }, Actor);
        await licenses.ActivateAsync(license.LicenseKey, "INST-GYM-1", null, "10.0.0.8");

        await svc.CreateTicketAsync(new CreateSupportTicketRequest
        {
            CustomerId = customer.Id,
            ContractId = contract.Id,
            LocalLicenseId = license.Id,
            Subject = "Reception printer not working",
            Description = "Front desk printer offline.",
        }, Actor);

        var profile = await svc.GetProfileAsync(customer.Id);
        Assert.Equal("Fitness Gym", profile!.Customer.BusinessName);
        Assert.Equal("HY-CTR-", profile.LatestContract!.ContractNumber[..7]);
        Assert.Equal(4, profile.PurchasedItems.Count);
        Assert.Equal("partial", profile.PaymentStatus);
        Assert.Equal(15000m, profile.PaidAmount);
        Assert.Equal(14500m, profile.OutstandingAmount);
        Assert.Equal(license.LicenseKey, profile.License!.LicenseKey);
        Assert.Equal("INST-GYM-1", profile.Installation!.InstallationId);
        Assert.Equal(1, profile.OpenSupportTicketCount);
        Assert.Single(profile.Licenses);
        Assert.Equal(license.Id, profile.Licenses[0].Id);
    }

    [Fact]
    public async Task UpdateTicket_AssignsActivePlatformUser()
    {
        var (svc, _, _) = NewSut();
        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Fitness Gym", OwnerName = "Ahmed" }, Actor);
        var ticket = await svc.CreateTicketAsync(new CreateSupportTicketRequest
        {
            CustomerId = customer.Id,
            Subject = "Need help",
            Description = "Front desk cannot print.",
        }, Actor);

        var updated = await svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest
        {
            AssignedToPlatformAdminUserId = Actor,
        }, Actor);

        Assert.Equal(Actor, updated!.AssignedToPlatformAdminUserId);
        Assert.Equal("open", updated.Status);
    }

    [Fact]
    public async Task UpdateTicket_RejectsMissingAssignee_WithoutMutating()
    {
        var (svc, db, _) = NewSut();
        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Fitness Gym", OwnerName = "Ahmed" }, Actor);
        var ticket = await svc.CreateTicketAsync(new CreateSupportTicketRequest
        {
            CustomerId = customer.Id,
            Subject = "Need help",
            Description = "Front desk cannot print.",
        }, Actor);

        var missing = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest
            {
                Status = PlatformSupportTicketStatuses.InProgress,
                AssignedToPlatformAdminUserId = missing,
            }, Actor));
        Assert.Equal("Assignee was not found.", ex.Message);

        var stored = await db.SupportTickets.AsNoTracking().FirstAsync(t => t.Id == ticket.Id);
        Assert.Equal(PlatformSupportTicketStatuses.Open, stored.Status);
        Assert.Null(stored.AssignedToPlatformAdminUserId);
    }

    [Fact]
    public async Task UpdateTicket_RejectsInactiveAssignee_WithoutMutating()
    {
        var (svc, db, _) = NewSut();
        var inactiveId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        SeedPlatformUser(db, inactiveId, isActive: false);
        var customer = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest { BusinessName = "Fitness Gym", OwnerName = "Ahmed" }, Actor);
        var ticket = await svc.CreateTicketAsync(new CreateSupportTicketRequest
        {
            CustomerId = customer.Id,
            Subject = "Need help",
            Description = "Front desk cannot print.",
        }, Actor);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateTicketAsync(ticket.Id, new UpdateSupportTicketRequest
            {
                AssignedToPlatformAdminUserId = inactiveId,
            }, Actor));
        Assert.Equal("Assignee is inactive.", ex.Message);

        var stored = await db.SupportTickets.AsNoTracking().FirstAsync(t => t.Id == ticket.Id);
        Assert.Null(stored.AssignedToPlatformAdminUserId);
    }

    [Fact]
    public async Task UpdateCustomer_AuditPayloadIncludesIdStatusAndTenantId()
    {
        var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase("cust-audit-" + Guid.NewGuid())
            .Options);
        SeedPlatformUser(db, Actor, isActive: true);
        var licenses = new LocalLicenseService(new LocalLicenseWriteRepository(db), new NullSigner(), db);
        var audit = new PlatformAuditService(db, new HttpContextAccessor(), NullLogger<PlatformAuditService>.Instance);
        var svc = new PlatformCustomerService(db, audit, licenses);

        var created = await svc.CreateCustomerAsync(new UpsertPlatformCustomerRequest
        {
            BusinessName = "Fitness Gym",
            OwnerName = "Ahmed",
        }, Actor);

        await svc.UpdateCustomerAsync(created.Id, new UpsertPlatformCustomerRequest
        {
            BusinessName = created.BusinessName,
            OwnerName = created.OwnerName,
            Status = PlatformCustomerStatuses.Active,
        }, Actor);

        var statusUpdate = await db.PlatformAuditLogs.AsNoTracking()
            .Where(a => a.Action == "platform.customer.updated")
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstAsync();
        Assert.Equal(Actor, statusUpdate.ActorPlatformUserId);
        using (var after = System.Text.Json.JsonDocument.Parse(statusUpdate.AfterJson!))
        {
            Assert.Equal(created.Id.ToString(), after.RootElement.GetProperty("Id").GetString());
            Assert.Equal(PlatformCustomerStatuses.Active, after.RootElement.GetProperty("Status").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, after.RootElement.GetProperty("TenantId").ValueKind);
        }

        var tenantId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        await svc.SetCustomerCloudLinkAsync(created.Id, tenantId, Actor);

        var linked = await db.PlatformAuditLogs.AsNoTracking()
            .Where(a => a.Action == "platform.customer.cloud_link")
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstAsync();
        Assert.Equal(Actor, linked.ActorPlatformUserId);
        Assert.Equal(tenantId, linked.TenantId);
        Assert.True(linked.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-1));
        using (var after = System.Text.Json.JsonDocument.Parse(linked.AfterJson!))
        {
            Assert.Equal(created.Id.ToString(), after.RootElement.GetProperty("Id").GetString());
            Assert.Equal(tenantId.ToString(), after.RootElement.GetProperty("TenantId").GetString());
        }
        using (var before = System.Text.Json.JsonDocument.Parse(linked.BeforeJson!))
        {
            Assert.Equal(created.Id.ToString(), before.RootElement.GetProperty("Id").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, before.RootElement.GetProperty("TenantId").ValueKind);
        }

        // Ordinary update must not clear or change TenantId even if the body sends null / another id.
        await svc.UpdateCustomerAsync(created.Id, new UpsertPlatformCustomerRequest
        {
            BusinessName = created.BusinessName,
            OwnerName = created.OwnerName,
            Status = PlatformCustomerStatuses.Active,
            TenantId = null,
        }, Actor);

        var stillLinked = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.Equal(tenantId, stillLinked.TenantId);

        await svc.SetCustomerCloudLinkAsync(created.Id, null, Actor);

        var unlinked = await db.PlatformAuditLogs.AsNoTracking()
            .Where(a => a.Action == "platform.customer.cloud_link")
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstAsync();
        using var unlinkedBefore = System.Text.Json.JsonDocument.Parse(unlinked.BeforeJson!);
        using var unlinkedAfter = System.Text.Json.JsonDocument.Parse(unlinked.AfterJson!);
        Assert.Equal(tenantId.ToString(), unlinkedBefore.RootElement.GetProperty("TenantId").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, unlinkedAfter.RootElement.GetProperty("TenantId").ValueKind);
        Assert.Equal(created.Id.ToString(), unlinkedAfter.RootElement.GetProperty("Id").GetString());
        Assert.Null(unlinked.TenantId);
    }
}
