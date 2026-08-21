namespace GMS.Tests.Platform;

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Platform;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;
using GMS.Tests.Helpers;

public class SubscriptionCoreTests
{
    private static readonly string LocalDb =
        @"Server=(localdb)\mssqllocaldb;Database=GymFlowProDb_PlatformSubscriptionTests;Trusted_Connection=true;Encrypt=false;";

    [Fact]
    public async Task FilteredUniqueIndex_RejectsSecondLiveSubscription()
    {
        await using var db = await CreateRelationalDbAsync();
        var tenantId = Guid.NewGuid();
        var code = "CP1" + tenantId.ToString("N")[..8];
        var email = $"cp1-{tenantId:N}@test.local";

        await db.Database.ExecuteSqlInterpolatedAsync($@"
IF NOT EXISTS (SELECT 1 FROM dbo.tenants WHERE Id = {tenantId})
INSERT INTO dbo.tenants (Id)
VALUES ({tenantId});
");

        try
        {
            var repo = new SubscriptionWriteRepository(db);
            var first = NewLive(tenantId, SubscriptionStatuses.Trialing);
            await repo.SaveWithChangeAsync(first, TrialChange(first));

            var second = NewLive(tenantId, SubscriptionStatuses.Active);
            var ex = await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                repo.SaveWithChangeAsync(second, TrialChange(second)));

            var detail = ex.ToString() + (ex.InnerException?.Message ?? string.Empty);
            Assert.True(
                detail.Contains("UX_subscriptions_tenant_live", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("unique", StringComparison.OrdinalIgnoreCase),
                $"Expected unique index violation, got: {detail}");
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
DELETE FROM platform.subscription_changes WHERE TenantId = {tenantId};
DELETE FROM platform.subscriptions WHERE TenantId = {tenantId};
DELETE FROM dbo.tenants WHERE Id = {tenantId};
");
        }
    }

    [Fact]
    public async Task ChangeTier_AlwaysWritesSubscriptionChangeRow()
    {
        var (svc, db) = CreateInMemoryService();
        var tenantId = Guid.NewGuid();

        var start = await svc.StartTrialAsync(tenantId, PlanTiers.Growth);
        Assert.True(start.Success);

        var upgrade = await svc.ChangeTierAsync(tenantId, PlanTiers.Pro, effectiveNow: true);
        Assert.True(upgrade.Success);

        var changes = await db.SubscriptionChanges.Where(c => c.TenantId == tenantId).ToListAsync();
        Assert.Contains(changes, c => c.ChangeType == SubscriptionChangeTypes.TrialStart);
        Assert.Contains(changes, c => c.ChangeType == SubscriptionChangeTypes.Upgrade);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public async Task CancelAtPeriodEnd_DoesNotCancelStatusImmediately()
    {
        var (svc, _) = CreateInMemoryService();
        var tenantId = Guid.NewGuid();
        Assert.True((await svc.StartTrialAsync(tenantId)).Success);

        var cancel = await svc.CancelAsync(tenantId, immediate: false, reason: "switching");
        Assert.True(cancel.Success);
        Assert.Equal(SubscriptionStatuses.Trialing, cancel.Subscription!.Status);
        Assert.True(cancel.Subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task ImmediateCancel_RequiresReason()
    {
        var (svc, _) = CreateInMemoryService();
        var tenantId = Guid.NewGuid();
        Assert.True((await svc.StartTrialAsync(tenantId)).Success);

        var bad = await svc.CancelAsync(tenantId, immediate: true, reason: null);
        Assert.False(bad.Success);
        Assert.Equal("REASON_REQUIRED", bad.ErrorCode);
    }

    [Fact]
    public async Task GetStatusAsync_CacheHit_P99Under20ms()
    {
        var (svc, _) = CreateInMemoryService();
        var tenantId = Guid.NewGuid();
        Assert.True((await svc.StartTrialAsync(tenantId)).Success);

        // Warm cache
        _ = await svc.GetStatusAsync(tenantId);

        const int n = 500;
        var samples = new long[n];
        for (var i = 0; i < n; i++)
        {
            var sw = Stopwatch.StartNew();
            var status = await svc.GetStatusAsync(tenantId);
            sw.Stop();
            Assert.NotNull(status);
            samples[i] = sw.ElapsedMilliseconds;
        }

        Array.Sort(samples);
        var p99 = samples[(int)(n * 0.99) - 1];
        Assert.True(p99 < 20, $"GetStatusAsync cache-hit p99 was {p99}ms (limit 20ms)");
    }

    private static (ISubscriptionService Svc, PlatformDbContext Db) CreateInMemoryService()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase("cp1-" + Guid.NewGuid())
            .Options;
        var db = new PlatformDbContext(options);
        var repo = new SubscriptionWriteRepository(db);

        var memoryCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        var cache = new SubscriptionStatusCache(memoryCache, NullLogger<SubscriptionStatusCache>.Instance);
        var invoices = new NoopProrationInvoiceService();
        var audit = new NoopAudit();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformSubscription:TrialDays"] = "14"
            })
            .Build();

        var svc = new SubscriptionService(
            repo, cache, new AlwaysEnabledFeatureAccess(), invoices, audit, config, NullLogger<SubscriptionService>.Instance);
        return (svc, db);
    }

    private static async Task<PlatformDbContext> CreateRelationalDbAsync()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(LocalDb, sql =>
            {
                sql.MigrationsHistoryTable(
                    PlatformServiceExtensions.MigrationsHistoryTable,
                    PlatformServiceExtensions.Schema);
            })
            .Options;

        var db = new PlatformDbContext(options);
        await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID('dbo.tenants', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tenants (
        Id uniqueidentifier NOT NULL PRIMARY KEY
    );
END
""");
        await db.Database.MigrateAsync();
        return db;
    }

    private static PlatformSubscription NewLive(Guid tenantId, string status) => new()
    {
        TenantId = tenantId,
        PlanTier = PlanTiers.Growth,
        Status = status,
        BillingCycle = BillingCycles.Monthly,
        PriceEgp = 1999m,
        CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
        CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
        TrialEndsAtUtc = DateTime.UtcNow.AddDays(14)
    };

    private static SubscriptionChange TrialChange(PlatformSubscription s) => new()
    {
        TenantId = s.TenantId,
        SubscriptionId = s.Id,
        ChangeType = SubscriptionChangeTypes.TrialStart,
        ToTier = s.PlanTier,
        EffectiveAtUtc = DateTime.UtcNow,
        InitiatedBy = SubscriptionInitiators.System
    };

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
