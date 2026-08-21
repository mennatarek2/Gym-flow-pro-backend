namespace GMS.Tests.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

/// <summary>
/// CP8: MRR normalization (annual ÷12) + movement reconciliation invariant.
/// </summary>
public class Cp8PlatformMetricsTests
{
    private const string LocalDb =
        @"Server=(localdb)\mssqllocaldb;Database=GymFlowProDb_PlatformCp8Tests;Trusted_Connection=true;Encrypt=false;";

    [Fact]
    public void ToMonthlyMrr_NormalizesAnnualByDividingBy12()
    {
        Assert.Equal(1999m, PlatformMetricsService.ToMonthlyMrr(1999m, BillingCycles.Monthly));
        Assert.Equal(3332.50m, PlatformMetricsService.ToMonthlyMrr(39990m, BillingCycles.Annual));
        Assert.Equal(999.92m, PlatformMetricsService.ToMonthlyMrr(11999m, BillingCycles.Annual));
    }

    [Fact]
    public async Task GetMrr_MatchesManualSpotCheck_MonthlyPlusAnnual()
    {
        await EnsureSchemasAsync();
        var tMonthly = Guid.NewGuid();
        var tAnnual = Guid.NewGuid();
        var asOf = new DateOnly(2026, 7, 15);

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tMonthly));
            infra.Tenants.Add(NewTenant(tAnnual));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        platform.Subscriptions.Add(Sub(
            Guid.NewGuid(), tMonthly, PlanTiers.Growth, SubscriptionStatuses.Active, 1999m,
            BillingCycles.Monthly, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        platform.Subscriptions.Add(Sub(
            Guid.NewGuid(), tAnnual, PlanTiers.Pro, SubscriptionStatuses.Active, 39990m,
            BillingCycles.Annual, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await platform.SaveChangesAsync();

        var svc = CreateMetrics(platform);
        var snap = await svc.GetMrrAsync(asOf);

        var expected = 1999m + PlatformMetricsService.ToMonthlyMrr(39990m, BillingCycles.Annual);
        Assert.Equal(expected, snap.MrrEgp);
        Assert.Equal(Math.Round(expected * 12m, 2, MidpointRounding.AwayFromZero), snap.ArrEgp);
        Assert.Equal(2, snap.PayingTenantCount);

        await CleanupAsync(platform, tMonthly, tAnnual);
    }

    [Fact]
    public async Task Movement_Reconciles_NewPlusExpansionMinusContractionMinusChurn()
    {
        // Start: A growth 1999, B growth 1999 → MRR 3998
        // Period: B cancels (−1999), C new starter (+999)
        // End: A 1999 + C 999 = 2998
        // 3998 + 999 − 1999 = 2998 ✓
        await EnsureSchemasAsync();
        var tA = Guid.NewGuid();
        var tB = Guid.NewGuid();
        var tC = Guid.NewGuid();
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tA));
            infra.Tenants.Add(NewTenant(tB));
            infra.Tenants.Add(NewTenant(tC));
            await infra.SaveChangesAsync();
        }

        var subA = Guid.NewGuid();
        var subB = Guid.NewGuid();
        var subC = Guid.NewGuid();

        await using var platform = CreatePlatformDb();
        platform.Subscriptions.Add(Sub(subA, tA, PlanTiers.Growth, SubscriptionStatuses.Active, 1999m,
            BillingCycles.Monthly, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        platform.Subscriptions.Add(Sub(subB, tB, PlanTiers.Growth, SubscriptionStatuses.Cancelled, 1999m,
            BillingCycles.Monthly, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            cancelledUtc: new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)));
        platform.Subscriptions.Add(Sub(subC, tC, PlanTiers.Starter, SubscriptionStatuses.Active, 999m,
            BillingCycles.Monthly, new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc)));
        platform.SubscriptionChanges.Add(new SubscriptionChange
        {
            Id = Guid.NewGuid(),
            TenantId = tB,
            SubscriptionId = subB,
            ChangeType = SubscriptionChangeTypes.Cancellation,
            FromTier = PlanTiers.Growth,
            EffectiveAtUtc = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
            InitiatedBy = SubscriptionInitiators.System,
            CreatedAtUtc = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)
        });
        await platform.SaveChangesAsync();

        var svc = CreateMetrics(platform);
        var movement = await svc.GetMovementAsync(from, to);

        Assert.Equal(3998m, movement.StartingMrrEgp);
        Assert.Equal(999m, movement.NewMrrEgp);
        Assert.Equal(0m, movement.ExpansionMrrEgp);
        Assert.Equal(0m, movement.ContractionMrrEgp);
        Assert.Equal(1999m, movement.ChurnedMrrEgp);

        var applied = movement.StartingMrrEgp + movement.NewMrrEgp + movement.ExpansionMrrEgp
                      - movement.ContractionMrrEgp - movement.ChurnedMrrEgp;
        Assert.Equal(movement.EndingMrrEgp, Math.Round(applied, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(2998m, movement.EndingMrrDirectEgp);
        Assert.True(movement.Reconciles);

        await CleanupAsync(platform, tA, tB, tC);
    }

    [Fact]
    public async Task TierDistribution_GroupsPayingMrrByTier()
    {
        await EnsureSchemasAsync();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var asOf = new DateOnly(2026, 7, 1);

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(t1));
            infra.Tenants.Add(NewTenant(t2));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        platform.Subscriptions.Add(Sub(Guid.NewGuid(), t1, PlanTiers.Starter, SubscriptionStatuses.Active, 999m,
            BillingCycles.Monthly, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        platform.Subscriptions.Add(Sub(Guid.NewGuid(), t2, PlanTiers.Growth, SubscriptionStatuses.Active, 1999m,
            BillingCycles.Monthly, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await platform.SaveChangesAsync();

        var dist = await CreateMetrics(platform).GetTierDistributionAsync(asOf);
        Assert.Equal(2, dist.TotalPayingTenants);
        Assert.Equal(2998m, dist.TotalMrrEgp);
        Assert.Contains(dist.Tiers, r => r.PlanTier == PlanTiers.Starter && r.MrrEgp == 999m);
        Assert.Contains(dist.Tiers, r => r.PlanTier == PlanTiers.Growth && r.MrrEgp == 1999m);

        await CleanupAsync(platform, t1, t2);
    }

    private static PlatformMetricsService CreateMetrics(PlatformDbContext db)
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return new PlatformMetricsService(db, cache, NullLogger<PlatformMetricsService>.Instance);
    }

    private static PlatformSubscription Sub(
        Guid id, Guid tenantId, string tier, string status, decimal price,
        string cycle, DateTime createdUtc, DateTime? cancelledUtc = null) => new()
    {
        Id = id,
        TenantId = tenantId,
        PlanTier = tier,
        Status = status,
        BillingCycle = cycle,
        PriceEgp = price,
        CurrentPeriodStart = new DateOnly(2026, 7, 1),
        CurrentPeriodEnd = new DateOnly(2026, 7, 31),
        CreatedAtUtc = createdUtc,
        CancelledAtUtc = cancelledUtc,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Tenant NewTenant(Guid id) => new()
    {
        Id = id,
        Name = $"CP8-{id:N}"[..20],
        NameAr = "اختبار",
        GymCode = id.ToString("N")[..8].ToUpperInvariant(),
        City = "Cairo",
        Address = "Test",
        PhoneNumber = "+201000000000",
        Email = $"{id:N}@cp8.test",
        IsActive = true,
        SubscriptionStartDate = DateTime.UtcNow,
        Settings = "{}"
    };

    private static GymFlowProDbContext CreateInfraDb()
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseSqlServer(LocalDb)
            .Options;
        return new GymFlowProDbContext(options, new TestTenantContext());
    }

    private static PlatformDbContext CreatePlatformDb()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(LocalDb)
            .Options;
        return new PlatformDbContext(options);
    }

    private static async Task EnsureSchemasAsync()
    {
        await using var infra = CreateInfraDb();
        await infra.Database.MigrateAsync();
        await using var platform = CreatePlatformDb();
        await platform.Database.MigrateAsync();
    }

    private static async Task CleanupAsync(PlatformDbContext platform, params Guid[] tenantIds)
    {
        foreach (var tenantId in tenantIds)
        {
            await platform.SubscriptionChanges.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
            await platform.Subscriptions.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
        }

        await using var infra = CreateInfraDb();
        foreach (var tenantId in tenantIds)
            await infra.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
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
}
