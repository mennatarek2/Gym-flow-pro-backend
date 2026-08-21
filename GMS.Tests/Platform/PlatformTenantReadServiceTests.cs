using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Core.Entities;
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

    private static (ISubscriptionService Svc, PlatformDbContext Db) CreateSubscriptionService(PlatformDbContext db)
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformSubscription:TrialDays"] = "14"
            })
            .Build();

        var svc = new SubscriptionService(
            new SubscriptionWriteRepository(db),
            new SubscriptionStatusCache(cache, NullLogger<SubscriptionStatusCache>.Instance),
            new AlwaysOnFeatureAccess(),
            new NoopProrationInvoiceService(),
            new NoopAudit(),
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
