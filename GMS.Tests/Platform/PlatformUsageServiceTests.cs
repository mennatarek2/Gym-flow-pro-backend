namespace GMS.Tests.Platform;

using Microsoft.EntityFrameworkCore;
using GMS.Platform.Entities;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

public class PlatformUsageServiceTests
{
    private static PlatformDbContext CreateInMemoryDb() =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task GetSummaryAsync_SumsTotalsAcrossTenants_ForCurrentPeriodOnly()
    {
        await using var db = CreateInMemoryDb();
        var period = TierEnforcementService.CurrentPeriodCairo();
        var priorPeriod = "2020-01"; // deliberately outside current period, must be excluded
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        db.UsageCounters.AddRange(
            new UsageCounter { TenantId = tenantA, Period = period, Metric = "active_members", Count = 100, Cap = 500 },
            new UsageCounter { TenantId = tenantB, Period = period, Metric = "active_members", Count = 50, Cap = 200 },
            new UsageCounter { TenantId = tenantA, Period = period, Metric = "staff_seats", Count = 3, Cap = 3 },
            new UsageCounter { TenantId = tenantA, Period = priorPeriod, Metric = "active_members", Count = 9999, Cap = 500 });
        await db.SaveChangesAsync();

        var svc = new PlatformUsageService(db);
        var summary = await svc.GetSummaryAsync();

        Assert.Equal(period, summary.Period);
        var membersTotal = summary.Totals.Single(t => t.Metric == "active_members");
        Assert.Equal(150, membersTotal.TotalCount);
        Assert.Equal(2, membersTotal.TenantCount);
    }

    [Fact]
    public async Task GetSummaryAsync_FlagsTenantsAtOrAboveEightyPercentOfCap()
    {
        await using var db = CreateInMemoryDb();
        var period = TierEnforcementService.CurrentPeriodCairo();
        var atLimit = Guid.NewGuid();
        var comfortable = Guid.NewGuid();

        db.UsageCounters.AddRange(
            new UsageCounter { TenantId = atLimit, Period = period, Metric = "staff_seats", Count = 8, Cap = 10 }, // 80%
            new UsageCounter { TenantId = comfortable, Period = period, Metric = "staff_seats", Count = 3, Cap = 10 }); // 30%
        await db.SaveChangesAsync();

        var svc = new PlatformUsageService(db);
        var summary = await svc.GetSummaryAsync();

        var row = Assert.Single(summary.TenantsNearLimit);
        Assert.Equal(atLimit, row.TenantId);
        Assert.Equal(80, row.PercentOfCap);
    }

    [Fact]
    public async Task GetSummaryAsync_TreatsUnlimitedCapsAsNeverNearLimit()
    {
        await using var db = CreateInMemoryDb();
        var period = TierEnforcementService.CurrentPeriodCairo();
        var tenantId = Guid.NewGuid();

        db.UsageCounters.Add(new UsageCounter
        {
            TenantId = tenantId, Period = period, Metric = "active_members", Count = 50000, Cap = null
        });
        await db.SaveChangesAsync();

        var svc = new PlatformUsageService(db);
        var summary = await svc.GetSummaryAsync();

        Assert.Empty(summary.TenantsNearLimit);
        Assert.Equal(50000, summary.Totals.Single().TotalCount);
    }

    [Fact]
    public async Task GetSummaryAsync_NoDataForPeriod_ReturnsEmptyNotError()
    {
        await using var db = CreateInMemoryDb();

        var svc = new PlatformUsageService(db);
        var summary = await svc.GetSummaryAsync();

        Assert.Empty(summary.Totals);
        Assert.Empty(summary.TenantsNearLimit);
    }
}
