namespace GMS.Tests.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Models;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;
using GMS.Platform;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;
using GMS.Tests.Helpers;

/// <summary>P1 — custom trial days, convert trial → paid, restart paid after cancel.</summary>
public class SubscriptionLifecycleP1Tests
{
    private const string LocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb_PlatformLifecycleP1Tests;Trusted_Connection=true;Encrypt=false;";

    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
    private static readonly Guid ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task StartTrial_DefaultTrialDays_UsesConfig14()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            var svc = CreateSubscriptionService(platform);
            var result = await svc.StartTrialAsync(tenantId, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId);
            Assert.True(result.Success);
            var sub = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            Assert.Equal(SubscriptionStatuses.Trialing, sub.Status);
            var expectedEnd = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddDays(14), CairoTimeZone));
            Assert.Equal(expectedEnd, sub.CurrentPeriodEnd);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(90)]
    public async Task StartTrial_CustomTrialDays_SetsPeriodEnd(int days)
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            var svc = CreateSubscriptionService(platform);
            var result = await svc.StartTrialAsync(
                tenantId, PlanTiers.Starter, SubscriptionInitiators.PlatformAdmin, ActorId, days);
            Assert.True(result.Success);
            var sub = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            var expectedEnd = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddDays(days), CairoTimeZone));
            Assert.Equal(expectedEnd, sub.CurrentPeriodEnd);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task StartTrial_RejectsInvalidTrialDays(int days)
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            var svc = CreateSubscriptionService(platform);
            var result = await svc.StartTrialAsync(
                tenantId, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId, days);
            Assert.False(result.Success);
            Assert.Equal("INVALID_TRIAL_DAYS", result.ErrorCode);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task StartTrial_IsTenantScoped()
    {
        await EnsureSchemasAsync();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, a);
        await SeedTenantAsync(infra, b);
        try
        {
            var svc = CreateSubscriptionService(platform);
            Assert.True((await svc.StartTrialAsync(a, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId, 7)).Success);
            var bad = await svc.StartTrialAsync(b, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId, 7);
            Assert.True(bad.Success);
            Assert.Equal(1, await platform.Subscriptions.CountAsync(s => s.TenantId == a));
            Assert.Equal(1, await platform.Subscriptions.CountAsync(s => s.TenantId == b));
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { a, b });
        }
    }

    [Fact]
    public async Task ConvertTrial_ToActive_ClearsTrialEndsAt_PreservesPriceAndPlan()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            var svc = CreateSubscriptionService(platform);
            await svc.StartTrialAsync(tenantId, PlanTiers.Pro, SubscriptionInitiators.PlatformAdmin, ActorId, 14);
            var before = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            var price = before.PriceEgp;

            var result = await svc.ConvertTrialToPaidAsync(tenantId, "contract signed", SubscriptionInitiators.PlatformAdmin, ActorId);
            Assert.True(result.Success);

            var sub = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            Assert.Equal(SubscriptionStatuses.Active, sub.Status);
            Assert.Null(sub.TrialEndsAtUtc);
            Assert.Equal(PlanTiers.Pro, sub.PlanTier);
            Assert.Equal(price, sub.PriceEgp);
            Assert.Contains(
                await platform.SubscriptionChanges.Where(c => c.TenantId == tenantId).ToListAsync(),
                c => c.ChangeType == SubscriptionChangeTypes.Reactivation);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task ConvertTrial_RequiresReason()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            var svc = CreateSubscriptionService(platform);
            await svc.StartTrialAsync(tenantId, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId);
            var bad = await svc.ConvertTrialToPaidAsync(tenantId, "  ", SubscriptionInitiators.PlatformAdmin, ActorId);
            Assert.False(bad.Success);
            Assert.Equal("REASON_REQUIRED", bad.ErrorCode);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task ConvertTrial_RejectsActiveSubscription()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            var svc = CreateSubscriptionService(platform);
            await svc.StartTrialAsync(tenantId, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId);
            await svc.ConvertTrialToPaidAsync(tenantId, "first", SubscriptionInitiators.PlatformAdmin, ActorId);
            var dup = await svc.ConvertTrialToPaidAsync(tenantId, "again", SubscriptionInitiators.PlatformAdmin, ActorId);
            Assert.False(dup.Success);
            Assert.Equal("NOT_TRIALING", dup.ErrorCode);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task ConvertTrial_RejectsWrongTenant()
    {
        await EnsureSchemasAsync();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, a);
        await SeedTenantAsync(infra, b);
        try
        {
            var svc = CreateSubscriptionService(platform);
            await svc.StartTrialAsync(a, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId);
            var bad = await svc.ConvertTrialToPaidAsync(b, "wrong tenant", SubscriptionInitiators.PlatformAdmin, ActorId);
            Assert.False(bad.Success);
            Assert.Equal("NO_LIVE_SUBSCRIPTION", bad.ErrorCode);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { a, b });
        }
    }

    [Fact]
    public async Task RestartPaid_CreatesNewActiveSubscription_LeavesCancelledRow()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            var svc = CreateSubscriptionService(platform);
            await svc.StartTrialAsync(tenantId, PlanTiers.Starter, SubscriptionInitiators.PlatformAdmin, ActorId, 7);
            await svc.CancelAsync(tenantId, true, "test cancel", SubscriptionInitiators.PlatformAdmin, ActorId);

            var cancelled = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            Assert.Equal(SubscriptionStatuses.Cancelled, cancelled.Status);

            var result = await svc.RestartPaidAsync(
                tenantId, PlanTiers.Growth, "customer returned", SubscriptionInitiators.PlatformAdmin, ActorId);
            Assert.True(result.Success);

            var rows = await platform.Subscriptions.Where(s => s.TenantId == tenantId).OrderBy(s => s.CreatedAtUtc).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal(SubscriptionStatuses.Cancelled, rows[0].Status);
            Assert.Equal(SubscriptionStatuses.Active, rows[1].Status);
            Assert.NotEqual(rows[0].Id, rows[1].Id);
            Assert.Equal(PlatformListPrices.MonthlyEgp(PlanTiers.Growth), rows[1].PriceEgp);
            Assert.Equal(PlanTiers.Growth, rows[1].PlanTier);
            Assert.Null(rows[1].TrialEndsAtUtc);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task RestartPaid_RequiresReason()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            var svc = CreateSubscriptionService(platform);
            await svc.StartTrialAsync(tenantId, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId);
            await svc.CancelAsync(tenantId, true, "cancel", SubscriptionInitiators.PlatformAdmin, ActorId);
            var bad = await svc.RestartPaidAsync(tenantId, PlanTiers.Growth, " ", SubscriptionInitiators.PlatformAdmin, ActorId);
            Assert.False(bad.Success);
            Assert.Equal("REASON_REQUIRED", bad.ErrorCode);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task RestartPaid_RejectsWhenLiveSubscriptionExists()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            var svc = CreateSubscriptionService(platform);
            await svc.StartTrialAsync(tenantId, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId);
            var bad = await svc.RestartPaidAsync(tenantId, PlanTiers.Pro, "nope", SubscriptionInitiators.PlatformAdmin, ActorId);
            Assert.False(bad.Success);
            Assert.Equal("LIVE_SUBSCRIPTION_EXISTS", bad.ErrorCode);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task RestartPaid_RejectsWrongTenant()
    {
        await EnsureSchemasAsync();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, a);
        await SeedTenantAsync(infra, b);
        try
        {
            var svc = CreateSubscriptionService(platform);
            await svc.StartTrialAsync(a, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId);
            await svc.CancelAsync(a, true, "cancel", SubscriptionInitiators.PlatformAdmin, ActorId);
            var bad = await svc.RestartPaidAsync(b, PlanTiers.Growth, "wrong", SubscriptionInitiators.PlatformAdmin, ActorId);
            Assert.False(bad.Success);
            Assert.Equal("NO_CANCELLED_SUBSCRIPTION", bad.ErrorCode);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { a, b });
        }
    }

    private static SubscriptionService CreateSubscriptionService(PlatformDbContext platform)
    {
        var repo = new SubscriptionWriteRepository(platform);
        var memory = new MemoryCache(new MemoryCacheOptions());
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cache = new SubscriptionStatusCache(distributed, NullLogger<SubscriptionStatusCache>.Instance);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PlatformSubscription:TrialDays"] = "14" })
            .Build();
        var audit = new NoOpAudit();
        PlatformCommercialPlanTestHelper.SeedCommercialPlansAsync(platform).GetAwaiter().GetResult();
        var commercialPlans = PlatformCommercialPlanTestHelper.CreatePlanService(platform, audit);
        return new SubscriptionService(
            repo,
            cache,
            new NoopFeatureAccess(),
            new NoopProration(),
            audit,
            commercialPlans,
            config,
            NullLogger<SubscriptionService>.Instance);
    }

    private static GymFlowProDbContext CreateInfraDb()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Guid.NewGuid(), "Platform P1 Test", "Africa/Cairo");
        return new GymFlowProDbContext(
            new DbContextOptionsBuilder<GymFlowProDbContext>().UseSqlServer(LocalDbConnectionString).Options,
            tenantContext);
    }

    private static PlatformDbContext CreatePlatformDb()
    {
        return new PlatformDbContext(
            new DbContextOptionsBuilder<PlatformDbContext>()
                .UseSqlServer(LocalDbConnectionString, sql =>
                    sql.MigrationsHistoryTable(PlatformServiceExtensions.MigrationsHistoryTable, PlatformServiceExtensions.Schema))
                .Options);
    }

    private static async Task EnsureSchemasAsync()
    {
        await using var infra = CreateInfraDb();
        await infra.Database.MigrateAsync();
        await using var platform = CreatePlatformDb();
        await platform.Database.MigrateAsync();
    }

    private static async Task SeedTenantAsync(GymFlowProDbContext infra, Guid tenantId)
    {
        infra.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "P1 Test Gym",
            NameAr = "اختبار",
            GymCode = $"GYM-{tenantId:N}".Substring(0, 13),
            City = "Cairo",
            Address = "Test",
            PhoneNumber = "+201000000000",
            Email = $"{tenantId:N}@test.local",
            SubscriptionStartDate = DateTime.UtcNow
        });
        await infra.SaveChangesAsync();
    }

    private static async Task CleanupAsync(
        PlatformDbContext platform,
        GymFlowProDbContext infra,
        IEnumerable<Guid> tenantIds)
    {
        var ids = tenantIds.ToArray();
        await platform.SubscriptionChanges.Where(c => ids.Contains(c.TenantId)).ExecuteDeleteAsync();
        await platform.Subscriptions.Where(s => ids.Contains(s.TenantId)).ExecuteDeleteAsync();
        await platform.PlatformAuditLogs.Where(a => ids.Contains(a.TenantId ?? Guid.Empty)).ExecuteDeleteAsync();
        await infra.Tenants.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync();
    }

    private sealed class NoOpAudit : IPlatformAuditService
    {
        public Task LogAsync(
            Guid actorPlatformUserId, string action, Guid? tenantId = null,
            object? before = null, object? after = null, string? ipAddress = null) => Task.CompletedTask;

        public Task<GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto>> ListAsync(
            Guid? tenantId, string? action, DateOnly? from, DateOnly? to, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto>
            {
                Page = page,
                PageSize = pageSize
            });
    }

    private sealed class NoopFeatureAccess : IFeatureAccessService
    {
        public Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopProration : IPlatformProrationInvoiceService
    {
        public Task<PlatformInvoice> CreateUpgradeProrationStubAsync(
            Guid tenantId, Guid subscriptionId, decimal proratedAmountEgp,
            string fromTier, string toTier, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformInvoice { TenantId = tenantId, SubscriptionId = subscriptionId });
    }
}
