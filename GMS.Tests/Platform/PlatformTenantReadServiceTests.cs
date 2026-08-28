using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Core.Entities;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Platform;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

namespace GMS.Tests.Platform;

public class PlatformTenantReadServiceTests
{
    private const string LocalDb =
        @"Server=(localdb)\mssqllocaldb;Database=GymFlowProDb_PlatformTenantReadTests;Trusted_Connection=true;Encrypt=false;";

    [Fact]
    public async Task GetSubscriptionChangesAsync_ReturnsNewestFirst()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PlatformDbContext(options);
        var tenantId = Guid.NewGuid();
        var subId = Guid.NewGuid();

        db.Subscriptions.Add(new PlatformSubscription
        {
            Id = subId,
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = SubscriptionStatuses.Active,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20))
        });
        db.SubscriptionChanges.AddRange(
            new SubscriptionChange
            {
                TenantId = tenantId,
                SubscriptionId = subId,
                ChangeType = SubscriptionChangeTypes.TrialStart,
                ToTier = PlanTiers.Growth,
                EffectiveAtUtc = DateTime.UtcNow.AddDays(-10),
                InitiatedBy = SubscriptionInitiators.System
            },
            new SubscriptionChange
            {
                TenantId = tenantId,
                SubscriptionId = subId,
                ChangeType = SubscriptionChangeTypes.Upgrade,
                FromTier = PlanTiers.Growth,
                ToTier = PlanTiers.Pro,
                EffectiveAtUtc = DateTime.UtcNow.AddDays(-1),
                InitiatedBy = SubscriptionInitiators.PlatformAdmin
            });
        await db.SaveChangesAsync();

        var svc = new PlatformTenantReadService(db, new SubscriptionWriteRepository(db));
        var changes = await svc.GetSubscriptionChangesAsync(tenantId);

        Assert.Equal(2, changes.Count);
        Assert.Equal(SubscriptionChangeTypes.Upgrade, changes[0].ChangeType);
    }

    [Fact]
    public async Task GetInvoicesAsync_MapsPlatformInvoices()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PlatformDbContext(options);
        var tenantId = Guid.NewGuid();
        var subId = Guid.NewGuid();

        db.PlatformInvoices.Add(new PlatformInvoice
        {
            TenantId = tenantId,
            SubscriptionId = subId,
            InvoiceNumber = "GFP-2026-000099",
            PeriodStart = new DateOnly(2026, 7, 1),
            PeriodEnd = new DateOnly(2026, 7, 31),
            Subtotal = 1999m,
            VatAmount = 0m,
            Total = 1999m,
            Status = "paid",
            DueDate = new DateOnly(2026, 7, 8),
            PdfUrl = "/uploads/x.pdf"
        });
        await db.SaveChangesAsync();

        var svc = new PlatformTenantReadService(db, new SubscriptionWriteRepository(db));
        var invoices = await svc.GetInvoicesAsync(tenantId);

        Assert.Single(invoices);
        Assert.Equal("GFP-2026-000099", invoices[0].InvoiceNumber);
        Assert.Equal("/uploads/x.pdf", invoices[0].PdfUrl);
    }

    [Fact]
    public async Task ListAsync_AfterStartTrial_ReturnsNonNullPlanTierAndStatus()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "PF12 List Gym",
                NameAr = "صالة",
                GymCode = $"P12{tenantId.ToString("N")[..6]}",
                City = "Cairo",
                Address = "Test",
                PhoneNumber = "+201000000000",
                Email = $"{tenantId:N}@pf12.test",
                IsActive = true,
                SubscriptionStartDate = DateTime.UtcNow,
                Settings = "{}"
            });
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        try
        {
            var (subscriptions, _) = CreateSubscriptionService(platform);
            var start = await subscriptions.StartTrialAsync(
                tenantId, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, Guid.NewGuid());
            Assert.True(start.Success, start.ErrorMessage);

            var readers = new PlatformTenantReadService(platform, new SubscriptionWriteRepository(platform));
            var page = await readers.ListAsync(null, null, null, "PF12 List", 1, 20);

            var row = Assert.Single(page.Items);
            Assert.Equal(tenantId, row.Id);
            Assert.Equal(PlanTiers.Growth, row.PlanTier);
            Assert.Equal(SubscriptionStatuses.Trialing, row.Status);
            Assert.NotNull(row.PriceEgp);
        }
        finally
        {
            await platform.SubscriptionChanges.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
            await platform.Subscriptions.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
            await platform.PlatformAuditLogs.Where(a => a.TenantId == tenantId).ExecuteDeleteAsync();
            await using var infra = CreateInfraDb();
            await infra.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ListAsync_RenewingBefore_FiltersToTenantsExpiringOnOrBeforeDate()
    {
        await EnsureSchemasAsync();
        var expiringSoonId = Guid.NewGuid();
        var expiringLaterId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.AddRange(
                new Tenant
                {
                    Id = expiringSoonId, Name = "PF13 Expiring Soon", NameAr = "صالة",
                    GymCode = $"PF13A{expiringSoonId.ToString("N")[..5]}", City = "Cairo", Address = "Test",
                    PhoneNumber = "+201000000002", Email = $"{expiringSoonId:N}@pf13.test",
                    IsActive = true, SubscriptionStartDate = DateTime.UtcNow, Settings = "{}"
                },
                new Tenant
                {
                    Id = expiringLaterId, Name = "PF13 Expiring Later", NameAr = "صالة",
                    GymCode = $"PF13B{expiringLaterId.ToString("N")[..5]}", City = "Cairo", Address = "Test",
                    PhoneNumber = "+201000000003", Email = $"{expiringLaterId:N}@pf13.test",
                    IsActive = true, SubscriptionStartDate = DateTime.UtcNow, Settings = "{}"
                });
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        try
        {
            platform.Subscriptions.AddRange(
                new PlatformSubscription
                {
                    TenantId = expiringSoonId, PlanTier = PlanTiers.Growth, Status = SubscriptionStatuses.Active,
                    BillingCycle = BillingCycles.Monthly, PriceEgp = 1999m,
                    CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-25)),
                    CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))
                },
                new PlatformSubscription
                {
                    TenantId = expiringLaterId, PlanTier = PlanTiers.Growth, Status = SubscriptionStatuses.Active,
                    BillingCycle = BillingCycles.Monthly, PriceEgp = 1999m,
                    CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                    CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(25))
                });
            await platform.SaveChangesAsync();

            var readers = new PlatformTenantReadService(platform, new SubscriptionWriteRepository(platform));
            var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
            var page = await readers.ListAsync(null, null, null, "PF13", 1, 20, renewingBefore: cutoff);

            Assert.Single(page.Items);
            Assert.Equal(expiringSoonId, page.Items[0].Id);

            var unfiltered = await readers.ListAsync(null, null, null, "PF13", 1, 20);
            Assert.Equal(2, unfiltered.Items.Count);
        }
        finally
        {
            await platform.SubscriptionChanges.Where(c => c.TenantId == expiringSoonId || c.TenantId == expiringLaterId).ExecuteDeleteAsync();
            await platform.Subscriptions.Where(s => s.TenantId == expiringSoonId || s.TenantId == expiringLaterId).ExecuteDeleteAsync();
            await using var infra = CreateInfraDb();
            await infra.Tenants.Where(t => t.Id == expiringSoonId || t.Id == expiringLaterId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ListAsync_OwnerNameAndEmail_ReturnedFromActiveOwnerRoleAccount()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var inactiveOwnerId = Guid.NewGuid();
        var ownerRoleId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(new Tenant
            {
                Id = tenantId, Name = "PF14 Owner Gym", NameAr = "صالة",
                GymCode = $"PF14{tenantId.ToString("N")[..6]}", City = "Cairo", Address = "Test",
                PhoneNumber = "+201000000004", Email = $"{tenantId:N}@pf14.test",
                IsActive = true, SubscriptionStartDate = DateTime.UtcNow, Settings = "{}"
            });
            infra.Roles.Add(new Microsoft.AspNetCore.Identity.IdentityRole<Guid>
            { Id = ownerRoleId, Name = "Owner", NormalizedName = "OWNER" });
            infra.Users.AddRange(
                new ApplicationUser
                {
                    Id = ownerId, TenantId = tenantId, FirstName = "Nadia", LastName = "Kassem",
                    UserName = $"{ownerId:N}@pf14.test", NormalizedUserName = $"{ownerId:N}@PF14.TEST",
                    Email = $"{ownerId:N}@pf14.test", NormalizedEmail = $"{ownerId:N}@PF14.TEST",
                    IsActive = true, CreatedAtUtc = DateTime.UtcNow.AddDays(-1), UpdatedAtUtc = DateTime.UtcNow
                },
                new ApplicationUser
                {
                    // A disabled former owner must not win over the active one.
                    Id = inactiveOwnerId, TenantId = tenantId, FirstName = "Old", LastName = "Owner",
                    UserName = $"{inactiveOwnerId:N}@pf14.test", NormalizedUserName = $"{inactiveOwnerId:N}@PF14.TEST",
                    Email = $"{inactiveOwnerId:N}@pf14.test", NormalizedEmail = $"{inactiveOwnerId:N}@PF14.TEST",
                    IsActive = false, CreatedAtUtc = DateTime.UtcNow.AddDays(-30), UpdatedAtUtc = DateTime.UtcNow
                });
            infra.UserRoles.AddRange(
                new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = ownerId, RoleId = ownerRoleId },
                new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = inactiveOwnerId, RoleId = ownerRoleId });
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        try
        {
            var readers = new PlatformTenantReadService(platform, new SubscriptionWriteRepository(platform));
            var page = await readers.ListAsync(null, null, null, "PF14 Owner", 1, 20);

            var row = Assert.Single(page.Items);
            Assert.Equal("Nadia Kassem", row.OwnerName);
            Assert.Equal($"{ownerId:N}@pf14.test", row.OwnerEmail);
        }
        finally
        {
            await using var infra = CreateInfraDb();
            infra.UserRoles.RemoveRange(infra.UserRoles.Where(ur => ur.UserId == ownerId || ur.UserId == inactiveOwnerId));
            infra.Users.RemoveRange(infra.Users.Where(u => u.Id == ownerId || u.Id == inactiveOwnerId));
            infra.Roles.RemoveRange(infra.Roles.Where(r => r.Id == ownerRoleId));
            await infra.SaveChangesAsync();
            // Tenant derives from BaseEntity, whose SaveChanges interceptor turns Remove() into a soft
            // delete (IsDeleted = true) rather than a physical row removal. The platform read service's
            // raw SQL against dbo.tenants filters IsDeleted, so a soft-deleted row would still linger in
            // this test database and pollute later test runs' name-substring searches — ExecuteDeleteAsync
            // issues a real SQL DELETE instead, matching the pattern the pre-existing tests in this file use.
            await infra.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ListAsync_NoOwnerAccount_OwnerFieldsAreNull()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(new Tenant
            {
                Id = tenantId, Name = "PF15 No Owner Gym", NameAr = "صالة",
                GymCode = $"PF15{tenantId.ToString("N")[..6]}", City = "Cairo", Address = "Test",
                PhoneNumber = "+201000000005", Email = $"{tenantId:N}@pf15.test",
                IsActive = true, SubscriptionStartDate = DateTime.UtcNow, Settings = "{}"
            });
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        try
        {
            var readers = new PlatformTenantReadService(platform, new SubscriptionWriteRepository(platform));
            var page = await readers.ListAsync(null, null, null, "PF15 No Owner", 1, 20);

            var row = Assert.Single(page.Items);
            Assert.Null(row.OwnerName);
            Assert.Null(row.OwnerEmail);
            Assert.Null(row.MemberCount);
            Assert.Null(row.MemberCap);
        }
        finally
        {
            await using var infra = CreateInfraDb();
            await infra.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ListAsync_MemberCount_ReturnedFromCurrentPeriodActiveMembersUsageCounter()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        var otherPeriodTenantId = Guid.NewGuid();
        var currentPeriod = CurrentCairoPeriodForTest();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.AddRange(
                new Tenant
                {
                    Id = tenantId, Name = "PF16 Members Gym", NameAr = "صالة",
                    GymCode = $"PF16A{tenantId.ToString("N")[..5]}", City = "Cairo", Address = "Test",
                    PhoneNumber = "+201000000006", Email = $"{tenantId:N}@pf16.test",
                    IsActive = true, SubscriptionStartDate = DateTime.UtcNow, Settings = "{}"
                },
                new Tenant
                {
                    Id = otherPeriodTenantId, Name = "PF16 Stale Period Gym", NameAr = "صالة",
                    GymCode = $"PF16B{otherPeriodTenantId.ToString("N")[..5]}", City = "Cairo", Address = "Test",
                    PhoneNumber = "+201000000007", Email = $"{otherPeriodTenantId:N}@pf16.test",
                    IsActive = true, SubscriptionStartDate = DateTime.UtcNow, Settings = "{}"
                });
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        try
        {
            platform.UsageCounters.AddRange(
                new UsageCounter
                {
                    TenantId = tenantId, Period = currentPeriod, Metric = UsageMetrics.ActiveMembers,
                    Count = 1645, Cap = 2000
                },
                new UsageCounter
                {
                    // A prior-period row for a different tenant must never leak in as "current".
                    TenantId = otherPeriodTenantId, Period = "2020-01", Metric = UsageMetrics.ActiveMembers,
                    Count = 999, Cap = 1000
                },
                new UsageCounter
                {
                    // A different metric for the same tenant must not be confused with member count.
                    TenantId = tenantId, Period = currentPeriod, Metric = UsageMetrics.StaffSeats,
                    Count = 12, Cap = 30
                });
            await platform.SaveChangesAsync();

            var readers = new PlatformTenantReadService(platform, new SubscriptionWriteRepository(platform));
            var page = await readers.ListAsync(null, null, null, "PF16", 1, 20);

            Assert.Equal(2, page.Items.Count);
            var withMembers = page.Items.Single(r => r.Id == tenantId);
            Assert.Equal(1645, withMembers.MemberCount);
            Assert.Equal(2000, withMembers.MemberCap);

            var stalePeriod = page.Items.Single(r => r.Id == otherPeriodTenantId);
            Assert.Null(stalePeriod.MemberCount);
            Assert.Null(stalePeriod.MemberCap);
        }
        finally
        {
            await platform.UsageCounters
                .Where(c => c.TenantId == tenantId || c.TenantId == otherPeriodTenantId)
                .ExecuteDeleteAsync();
            await using var infra = CreateInfraDb();
            await infra.Tenants.Where(t => t.Id == tenantId || t.Id == otherPeriodTenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ListAsync_HasSubscriptionFalse_ReturnsTenantsWithoutAnySubscriptionRow()
    {
        await EnsureSchemasAsync();
        var withSubId = Guid.NewGuid();
        var orphanId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.AddRange(
                new Tenant
                {
                    Id = withSubId, Name = "PF17 With Sub", NameAr = "صالة",
                    GymCode = $"PF17A{withSubId.ToString("N")[..5]}", City = "Cairo", Address = "Test",
                    PhoneNumber = "+201000000010", Email = $"{withSubId:N}@pf17.test",
                    IsActive = true, SubscriptionStartDate = DateTime.UtcNow, Settings = "{}"
                },
                new Tenant
                {
                    Id = orphanId, Name = "PF17 Orphan", NameAr = "صالة",
                    GymCode = $"PF17B{orphanId.ToString("N")[..5]}", City = "Cairo", Address = "Test",
                    PhoneNumber = "+201000000011", Email = $"{orphanId:N}@pf17.test",
                    IsActive = true, SubscriptionStartDate = DateTime.UtcNow, Settings = "{}"
                });
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        try
        {
            platform.Subscriptions.Add(new PlatformSubscription
            {
                TenantId = withSubId,
                PlanTier = PlanTiers.Growth,
                Status = SubscriptionStatuses.Active,
                BillingCycle = BillingCycles.Monthly,
                PriceEgp = 1999m,
                CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(25))
            });
            await platform.SaveChangesAsync();

            var readers = new PlatformTenantReadService(platform, new SubscriptionWriteRepository(platform));
            var orphans = await readers.ListAsync(null, null, null, "PF17", 1, 20, hasSubscription: false);

            var row = Assert.Single(orphans.Items);
            Assert.Equal(orphanId, row.Id);
            Assert.Null(row.Status);
            Assert.Null(row.PlanTier);
        }
        finally
        {
            await platform.SubscriptionChanges.Where(c => c.TenantId == withSubId).ExecuteDeleteAsync();
            await platform.Subscriptions.Where(s => s.TenantId == withSubId).ExecuteDeleteAsync();
            await using var infra = CreateInfraDb();
            await infra.Tenants.Where(t => t.Id == withSubId || t.Id == orphanId).ExecuteDeleteAsync();
        }
    }

    private static string CurrentCairoPeriodForTest()
    {
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairo);
        return $"{local.Year:D4}-{local.Month:D2}";
    }

    [Fact]
    public async Task GetDetailAsync_UsersListsStaffOnly_ExcludesMemberRoleAndRoleless()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var rolelessId = Guid.NewGuid();
        var ownerRoleId = Guid.NewGuid();
        var memberRoleId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "PF-Users Gym",
                NameAr = "صالة",
                GymCode = $"PFU{tenantId.ToString("N")[..6]}",
                City = "Cairo",
                Address = "Test",
                PhoneNumber = "+201000000001",
                Email = $"{tenantId:N}@pfusers.test",
                IsActive = true,
                SubscriptionStartDate = DateTime.UtcNow,
                Settings = "{}"
            });

            infra.Roles.AddRange(
                new Microsoft.AspNetCore.Identity.IdentityRole<Guid> { Id = ownerRoleId, Name = "Owner", NormalizedName = "OWNER" },
                new Microsoft.AspNetCore.Identity.IdentityRole<Guid> { Id = memberRoleId, Name = "Member", NormalizedName = "MEMBER" });

            infra.Users.AddRange(
                new ApplicationUser
                {
                    Id = ownerId, TenantId = tenantId, FirstName = "Amr", LastName = "Owner",
                    UserName = $"{ownerId:N}@pfusers.test", NormalizedUserName = $"{ownerId:N}@PFUSERS.TEST",
                    Email = $"{ownerId:N}@pfusers.test", NormalizedEmail = $"{ownerId:N}@PFUSERS.TEST",
                    IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                },
                new ApplicationUser
                {
                    Id = memberId, TenantId = tenantId, FirstName = "Some", LastName = "Member",
                    UserName = $"{memberId:N}@pfusers.test", NormalizedUserName = $"{memberId:N}@PFUSERS.TEST",
                    Email = $"{memberId:N}@pfusers.test", NormalizedEmail = $"{memberId:N}@PFUSERS.TEST",
                    IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                },
                new ApplicationUser
                {
                    Id = rolelessId, TenantId = tenantId, FirstName = "No", LastName = "Role",
                    UserName = $"{rolelessId:N}@pfusers.test", NormalizedUserName = $"{rolelessId:N}@PFUSERS.TEST",
                    Email = $"{rolelessId:N}@pfusers.test", NormalizedEmail = $"{rolelessId:N}@PFUSERS.TEST",
                    IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                });

            infra.UserRoles.AddRange(
                new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = ownerId, RoleId = ownerRoleId },
                new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = memberId, RoleId = memberRoleId });

            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        try
        {
            var readers = new PlatformTenantReadService(platform, new SubscriptionWriteRepository(platform));
            var detail = await readers.GetDetailAsync(tenantId);

            Assert.NotNull(detail);
            var user = Assert.Single(detail!.Users);
            Assert.Equal(ownerId, user.Id);
            Assert.Equal("Amr Owner", user.FullName);
            Assert.Equal("Owner", user.Role);
            Assert.DoesNotContain(detail.Users, u => u.Id == memberId);
            Assert.DoesNotContain(detail.Users, u => u.Id == rolelessId);
        }
        finally
        {
            await using var infra = CreateInfraDb();
            infra.UserRoles.RemoveRange(infra.UserRoles.Where(ur => ur.UserId == ownerId || ur.UserId == memberId));
            infra.Users.RemoveRange(infra.Users.Where(u => u.Id == ownerId || u.Id == memberId || u.Id == rolelessId));
            infra.Roles.RemoveRange(infra.Roles.Where(r => r.Id == ownerRoleId || r.Id == memberRoleId));
            infra.Tenants.RemoveRange(infra.Tenants.Where(t => t.Id == tenantId));
            await infra.SaveChangesAsync();
        }
    }

    private static (ISubscriptionService Svc, PlatformDbContext Db) CreateSubscriptionService(PlatformDbContext db)
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformSubscription:TrialDays"] = "14"
            })
            .Build();

        var audit = new NoopAudit();
        PlatformCommercialPlanTestHelper.SeedCommercialPlansAsync(db).GetAwaiter().GetResult();
        var commercialPlans = PlatformCommercialPlanTestHelper.CreatePlanService(db, audit);

        var svc = new SubscriptionService(
            new SubscriptionWriteRepository(db),
            new SubscriptionStatusCache(cache, NullLogger<SubscriptionStatusCache>.Instance),
            new AlwaysOnFeatureAccess(),
            new NoopProrationInvoiceService(),
            audit,
            commercialPlans,
            config,
            NullLogger<SubscriptionService>.Instance);
        return (svc, db);
    }

    private static async Task EnsureSchemasAsync()
    {
        await using var infra = CreateInfraDb();
        await infra.Database.MigrateAsync();

        await using var platform = CreatePlatformDb();
        await platform.Database.MigrateAsync();
    }

    private static GymFlowProDbContext CreateInfraDb()
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseSqlServer(LocalDb)
            .Options;
        var tc = new TestTenantContext();
        tc.SetTenant(Guid.NewGuid(), "t", "Egypt Standard Time");
        return new GymFlowProDbContext(options, tc);
    }

    private static PlatformDbContext CreatePlatformDb()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(LocalDb, sql =>
            {
                sql.MigrationsHistoryTable(
                    PlatformServiceExtensions.MigrationsHistoryTable,
                    PlatformServiceExtensions.Schema);
            })
            .Options;
        return new PlatformDbContext(options);
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public string? TenantName { get; private set; }
        public string? TimeZone { get; private set; }
        public bool IsInitialized => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string tenantName, string timeZone)
        {
            TenantId = tenantId;
            TenantName = tenantName;
            TimeZone = timeZone;
        }
        public void Clear()
        {
            TenantId = Guid.Empty;
            TenantName = null;
            TimeZone = null;
        }
    }

    private sealed class AlwaysOnFeatureAccess : IFeatureAccessService
    {
        public Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopAudit : IPlatformAuditService
    {
        public Task LogAsync(
            Guid actorPlatformUserId,
            string action,
            Guid? tenantId = null,
            object? before = null,
            object? after = null,
            string? ipAddress = null) => Task.CompletedTask;

        public Task<GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto>> ListAsync(
            Guid? tenantId, string? action, DateOnly? from, DateOnly? to, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto> { Page = page, PageSize = pageSize });
    }

    private sealed class NoopProrationInvoiceService : IPlatformProrationInvoiceService
    {
        public Task<PlatformInvoice> CreateUpgradeProrationStubAsync(
            Guid tenantId,
            Guid subscriptionId,
            decimal proratedAmountEgp,
            string fromTier,
            string toTier,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformInvoice
            {
                TenantId = tenantId,
                SubscriptionId = subscriptionId,
                InvoiceNumber = "TEST-PRORATION",
                PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
                PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow),
                Subtotal = proratedAmountEgp,
                Total = proratedAmountEgp,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });
    }
}
