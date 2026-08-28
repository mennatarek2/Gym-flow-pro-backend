namespace GMS.Tests.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;
using GMS.Tests.Helpers;

public class CommercialPlanTests
{
    [Fact]
    public async Task AnnualPrice_EqualsMonthlyTimesTen()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var plans = PlatformCommercialPlanTestHelper.CreatePlanService(db);

        var annual = await plans.GetListPriceForCycleAsync(PlanTiers.Growth, BillingCycles.Annual);
        var monthly = await plans.GetListPriceForCycleAsync(PlanTiers.Growth, BillingCycles.Monthly);

        Assert.Equal(monthly * 10m, annual);
    }

    [Fact]
    public async Task PriceIncrease_DoesNotModifyExistingSubscriptionPriceEgp()
    {
        var (svc, db, plans, actor) = await CreateStackAsync();
        var tenantId = Guid.NewGuid();

        Assert.True((await svc.StartTrialAsync(tenantId, PlanTiers.Growth)).Success);
        var before = await db.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(1999m, before.PriceEgp);

        var update = await plans.UpdatePricingAsync(
            PlanTiers.Growth,
            new() { MonthlyPriceEgp = 2299m, Reason = "Commercial test repricing" },
            actor);
        Assert.True(update.Success);

        var after = await db.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(1999m, after.PriceEgp);
    }

    [Fact]
    public async Task NewStartTrial_ReceivesUpdatedListPrice()
    {
        var (svc, db, plans, actor) = await CreateStackAsync();
        await plans.UpdatePricingAsync(
            PlanTiers.Growth,
            new() { MonthlyPriceEgp = 2299m, Reason = "Commercial test repricing" },
            actor);

        var tenantId = Guid.NewGuid();
        Assert.True((await svc.StartTrialAsync(tenantId, PlanTiers.Growth)).Success);

        var sub = await db.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(2299m, sub.PriceEgp);
    }

    [Fact]
    public async Task RestartPaid_ReceivesUpdatedListPrice()
    {
        var (svc, db, plans, actor) = await CreateStackAsync();
        var tenantId = Guid.NewGuid();

        Assert.True((await svc.StartTrialAsync(tenantId, PlanTiers.Growth)).Success);
        Assert.True((await svc.CancelAsync(tenantId, immediate: true, reason: "test cancel for restart")).Success);

        await plans.UpdatePricingAsync(
            PlanTiers.Pro,
            new() { MonthlyPriceEgp = 4299m, Reason = "Commercial test repricing" },
            actor);

        var restart = await svc.RestartPaidAsync(tenantId, PlanTiers.Pro, "restart paid test reason");
        Assert.True(restart.Success);
        Assert.Equal(4299m, restart.Subscription!.PriceEgp);
    }

    [Fact]
    public async Task ChangeTierUpgrade_UsesCurrentListPrice()
    {
        var (svc, _, plans, actor) = await CreateStackAsync();
        var tenantId = Guid.NewGuid();

        Assert.True((await svc.StartTrialAsync(tenantId, PlanTiers.Growth)).Success);
        await plans.UpdatePricingAsync(
            PlanTiers.Pro,
            new() { MonthlyPriceEgp = 4299m, Reason = "Commercial test repricing" },
            actor);

        var upgrade = await svc.ChangeTierAsync(tenantId, PlanTiers.Pro, effectiveNow: true);
        Assert.True(upgrade.Success);
        Assert.Equal(4299m, upgrade.Subscription!.PriceEgp);
    }

    [Fact]
    public async Task DeactivatePlan_DoesNotModifyExistingSubscription()
    {
        var (svc, db, plans, actor) = await CreateStackAsync();
        var tenantId = Guid.NewGuid();

        Assert.True((await svc.StartTrialAsync(tenantId, PlanTiers.Starter)).Success);
        var before = await db.Subscriptions.SingleAsync(s => s.TenantId == tenantId);

        await plans.SetDefaultAsync(
            PlanTiers.Growth,
            new() { Reason = "Move default away from starter" },
            actor);

        var deactivate = await plans.SetSalesStatusAsync(
            PlanTiers.Starter,
            new() { IsActiveForSales = false, Reason = "Retire starter for new sales" },
            actor);
        Assert.True(deactivate.Success);

        var after = await db.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(before.PriceEgp, after.PriceEgp);
        Assert.Equal(before.PlanTier, after.PlanTier);
        Assert.Equal(before.Status, after.Status);
    }

    [Fact]
    public async Task DeactivatedPlan_BlocksNewStartTrial()
    {
        var (svc, _, plans, actor) = await CreateStackAsync();
        await plans.SetDefaultAsync(PlanTiers.Growth, new() { Reason = "Keep growth default" }, actor);
        await plans.SetSalesStatusAsync(
            PlanTiers.Starter,
            new() { IsActiveForSales = false, Reason = "Retire starter for new sales" },
            actor);

        var result = await svc.StartTrialAsync(Guid.NewGuid(), PlanTiers.Starter);
        Assert.False(result.Success);
        Assert.Equal("PLAN_NOT_FOR_SALES", result.ErrorCode);
    }

    [Fact]
    public async Task ExactlyOneDefaultPlan_AfterSetDefault()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var plans = PlatformCommercialPlanTestHelper.CreatePlanService(db);
        var actor = Guid.NewGuid();

        await plans.SetDefaultAsync(PlanTiers.Pro, new() { Reason = "Switch default to pro plan" }, actor);

        var rows = await db.CommercialPlans.ToListAsync();
        Assert.Single(rows, p => p.IsDefault);
        Assert.Equal(PlanTiers.Pro, rows.Single(p => p.IsDefault).Tier);
    }

    [Fact]
    public async Task LiveSubscriptionCount_IncludesTrialingActivePastDueSuspended()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var tenant = Guid.NewGuid();

        db.Subscriptions.AddRange(
            LiveSub(tenant, PlanTiers.Growth, SubscriptionStatuses.Trialing),
            LiveSub(Guid.NewGuid(), PlanTiers.Growth, SubscriptionStatuses.Active),
            LiveSub(Guid.NewGuid(), PlanTiers.Growth, SubscriptionStatuses.PastDue),
            LiveSub(Guid.NewGuid(), PlanTiers.Growth, SubscriptionStatuses.Suspended),
            LiveSub(Guid.NewGuid(), PlanTiers.Growth, SubscriptionStatuses.Cancelled));
        await db.SaveChangesAsync();

        var plans = PlatformCommercialPlanTestHelper.CreatePlanService(db);
        var list = await plans.ListAsync();
        var growth = list.Single(p => p.Tier == PlanTiers.Growth);

        Assert.Equal(4, growth.LiveSubscriptionCount);
    }

    [Fact]
    public async Task PricingMutation_WritesPlanChangeLogAndAudit()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var audit = new RecordingCommercialAudit();
        var plans = PlatformCommercialPlanTestHelper.CreatePlanService(db, audit);
        var actor = Guid.NewGuid();

        await plans.UpdatePricingAsync(
            PlanTiers.Growth,
            new() { MonthlyPriceEgp = 2199m, Reason = "Audit trail pricing test" },
            actor);

        var history = await plans.GetHistoryAsync(PlanTiers.Growth, 1, 10);
        Assert.Contains(history.Items, h => h.FieldName == "monthly_price_egp");
        Assert.Contains("platform.plan.price_changed", audit.Actions);
    }

    private static PlatformSubscription LiveSub(Guid tenantId, string tier, string status) => new()
    {
        TenantId = tenantId,
        PlanTier = tier,
        Status = status,
        BillingCycle = BillingCycles.Monthly,
        PriceEgp = 1999m,
        CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
        CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))
    };

    private static async Task<(ISubscriptionService Svc, PlatformDbContext Db, ICommercialPlanService Plans, Guid Actor)> CreateStackAsync()
    {
        var db = CreateDb();
        await SeedAsync(db);
        var audit = new RecordingCommercialAudit();
        var plans = PlatformCommercialPlanTestHelper.CreatePlanService(db, audit);
        var actor = Guid.NewGuid();

        var repo = new SubscriptionWriteRepository(db);
        var cache = new SubscriptionStatusCache(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<SubscriptionStatusCache>.Instance);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PlatformSubscription:TrialDays"] = "14" })
            .Build();

        var svc = new SubscriptionService(
            repo,
            cache,
            new AlwaysEnabledFeatureAccess(),
            new NoopProrationInvoiceService(),
            audit,
            plans,
            config,
            NullLogger<SubscriptionService>.Instance);

        return (svc, db, plans, actor);
    }

    private static PlatformDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase("commercial-plans-" + Guid.NewGuid())
            .Options;
        return new PlatformDbContext(options);
    }

    private static async Task SeedAsync(PlatformDbContext db)
    {
        await PlatformCommercialPlanTestHelper.SeedCommercialPlansAsync(db);
        await PlatformCommercialPlanTestHelper.SeedTierFeatureMapAsync(db);
    }

    private sealed class RecordingCommercialAudit : IPlatformAuditService
    {
        public List<string> Actions { get; } = new();

        public Task LogAsync(
            Guid actorPlatformUserId,
            string action,
            Guid? tenantId = null,
            object? before = null,
            object? after = null,
            string? ipAddress = null)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }

        public Task<GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto>> ListAsync(
            Guid? tenantId,
            string? action,
            DateOnly? from,
            DateOnly? to,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto>
            {
                Page = page,
                PageSize = pageSize
            });
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
            Task.FromResult(new PlatformInvoice { TenantId = tenantId, SubscriptionId = subscriptionId });
    }
}
