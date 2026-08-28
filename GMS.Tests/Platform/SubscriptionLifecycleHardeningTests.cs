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

/// <summary>
/// P0 renewal catch-up (CurrentPeriodEnd &lt;= today) + undo cancel-at-period-end.
/// </summary>
public class SubscriptionLifecycleHardeningTests
{
    private const string LocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb_PlatformLifecycleP0Tests;Trusted_Connection=true;Encrypt=false;";

    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    [Fact]
    public async Task RenewalJob_ProcessesSubscriptionEndingToday()
    {
        await EnsureSchemasAsync();
        var today = TodayCairo();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedActiveDueAsync(infra, platform, tenantId, today, periodEndOffsetDays: 0);
        try
        {
            var job = CreateRenewalJob(platform, new AlwaysPaidPaymentService());
            await job.ExecuteAsync();

            var sub = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            Assert.Equal(today.AddDays(1), sub.CurrentPeriodStart);
            Assert.Equal(SubscriptionStatuses.Active, sub.Status);
            Assert.Equal(1, await platform.PlatformInvoices.CountAsync(i => i.TenantId == tenantId));
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task RenewalJob_ProcessesSubscriptionEndingYesterday_CatchUp()
    {
        await EnsureSchemasAsync();
        var today = TodayCairo();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedActiveDueAsync(infra, platform, tenantId, today, periodEndOffsetDays: -1);
        try
        {
            var job = CreateRenewalJob(platform, new AlwaysPaidPaymentService());
            await job.ExecuteAsync();

            var sub = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            Assert.True(sub.CurrentPeriodEnd > today.AddDays(-1), "Period must advance past the missed end date.");
            Assert.Equal(1, await platform.PlatformInvoices.CountAsync(i => i.TenantId == tenantId));
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task RenewalJob_ProcessesSubscriptionEndingSeveralDaysAgo_CatchUp()
    {
        await EnsureSchemasAsync();
        var today = TodayCairo();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedActiveDueAsync(infra, platform, tenantId, today, periodEndOffsetDays: -5);
        try
        {
            var beforeEnd = (await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId)).CurrentPeriodEnd;
            var job = CreateRenewalJob(platform, new AlwaysPaidPaymentService());
            await job.ExecuteAsync();

            var sub = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            Assert.Equal(beforeEnd.AddDays(1), sub.CurrentPeriodStart);
            Assert.Equal(1, await platform.PlatformInvoices.CountAsync(i => i.TenantId == tenantId));
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task RenewalJob_DoesNotProcessFuturePeriodEnd()
    {
        await EnsureSchemasAsync();
        var today = TodayCairo();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedActiveDueAsync(infra, platform, tenantId, today, periodEndOffsetDays: 7);
        try
        {
            var before = await platform.Subscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenantId);
            var job = CreateRenewalJob(platform, new AlwaysPaidPaymentService());
            await job.ExecuteAsync();

            var after = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            Assert.Equal(before.CurrentPeriodStart, after.CurrentPeriodStart);
            Assert.Equal(before.CurrentPeriodEnd, after.CurrentPeriodEnd);
            Assert.Empty(await platform.PlatformInvoices.Where(i => i.TenantId == tenantId).ToListAsync());
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task RenewalJob_Rerun_DoesNotDuplicateInvoiceOrAdvancePeriodTwice()
    {
        await EnsureSchemasAsync();
        var today = TodayCairo();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedActiveDueAsync(infra, platform, tenantId, today, periodEndOffsetDays: -2);
        try
        {
            var job = CreateRenewalJob(platform, new AlwaysPaidPaymentService());
            await job.ExecuteAsync();
            var afterFirst = await platform.Subscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenantId);
            var invoiceCount1 = await platform.PlatformInvoices.CountAsync(i => i.TenantId == tenantId);
            var cycleChanges1 = await platform.SubscriptionChanges.CountAsync(c =>
                c.TenantId == tenantId && c.ChangeType == SubscriptionChangeTypes.CycleChange);

            await job.ExecuteAsync();
            await job.ExecuteAsync();

            var afterRerun = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            Assert.Equal(afterFirst.CurrentPeriodStart, afterRerun.CurrentPeriodStart);
            Assert.Equal(afterFirst.CurrentPeriodEnd, afterRerun.CurrentPeriodEnd);
            Assert.Equal(invoiceCount1, await platform.PlatformInvoices.CountAsync(i => i.TenantId == tenantId));
            Assert.Equal(cycleChanges1, await platform.SubscriptionChanges.CountAsync(c =>
                c.TenantId == tenantId && c.ChangeType == SubscriptionChangeTypes.CycleChange));
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task RenewalJob_TrialWithCard_BecomesActive_IncludingMissedDay()
    {
        await EnsureSchemasAsync();
        var today = TodayCairo();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        infra.Tenants.Add(NewTenant(tenantId));
        await infra.SaveChangesAsync();

        platform.Subscriptions.Add(new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = SubscriptionStatuses.Trialing,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = today.AddDays(-14),
            CurrentPeriodEnd = today.AddDays(-1),
            TrialEndsAtUtc = DateTime.UtcNow.AddDays(-1),
            SavedCardToken = "tok_test",
            AutoRenewOptIn = true
        });
        await platform.SaveChangesAsync();

        try
        {
            var job = CreateRenewalJob(platform, new AlwaysPaidPaymentService());
            await job.ExecuteAsync();

            var sub = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            Assert.Equal(SubscriptionStatuses.Active, sub.Status);
            Assert.Null(sub.TrialEndsAtUtc);
            Assert.Contains(
                await platform.SubscriptionChanges.Where(c => c.TenantId == tenantId).ToListAsync(),
                c => c.ChangeType == SubscriptionChangeTypes.Reactivation &&
                     c.Reason == "trial converted to paid");
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task RenewalJob_TrialWithoutCard_BecomesCancelled_IncludingMissedDay()
    {
        await EnsureSchemasAsync();
        var today = TodayCairo();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        infra.Tenants.Add(NewTenant(tenantId));
        await infra.SaveChangesAsync();

        platform.Subscriptions.Add(new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = SubscriptionStatuses.Trialing,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = today.AddDays(-14),
            CurrentPeriodEnd = today.AddDays(-3),
            TrialEndsAtUtc = DateTime.UtcNow.AddDays(-3)
        });
        await platform.SaveChangesAsync();

        try
        {
            var job = CreateRenewalJob(platform, new NeverHasCardPaymentService());
            await job.ExecuteAsync();
            await job.ExecuteAsync();

            var sub = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            Assert.Equal(SubscriptionStatuses.Cancelled, sub.Status);
            Assert.NotNull(sub.CancelledAtUtc);
            Assert.Empty(await platform.PlatformInvoices.Where(i => i.TenantId == tenantId).ToListAsync());
            Assert.Equal(1, await platform.SubscriptionChanges.CountAsync(c =>
                c.TenantId == tenantId && c.ChangeType == SubscriptionChangeTypes.Cancellation));
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task UndoCancelAtPeriodEnd_ClearsFlag_PreservesStatusPeriodAndPrice()
    {
        var (svc, db, audit) = CreateInMemorySubscriptionService();
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        Assert.True((await svc.StartTrialAsync(tenantId, PlanTiers.Pro, platformAdminUserId: actor)).Success);

        var sub = await db.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
        var periodEnd = sub.CurrentPeriodEnd;
        var price = sub.PriceEgp;
        var status = sub.Status;

        Assert.True((await svc.CancelAsync(tenantId, immediate: false, reason: "temporary", platformAdminUserId: actor)).Success);
        Assert.True((await db.Subscriptions.SingleAsync(s => s.TenantId == tenantId)).CancelAtPeriodEnd);

        var undo = await svc.UndoCancelAtPeriodEndAsync(tenantId, "customer changed mind", platformAdminUserId: actor);
        Assert.True(undo.Success, undo.ErrorMessage);
        Assert.False(undo.Subscription!.CancelAtPeriodEnd);
        Assert.Equal(status, undo.Subscription.Status);
        Assert.Equal(periodEnd, undo.Subscription.CurrentPeriodEnd);
        Assert.Equal(price, undo.Subscription.PriceEgp);

        Assert.Contains(db.SubscriptionChanges, c => c.ChangeType == SubscriptionChangeTypes.CancelUndo);
        Assert.Contains(audit.Entries, e => e.Action == "platform.subscription.undo_cancel_at_period_end");
    }

    [Fact]
    public async Task UndoCancelAtPeriodEnd_RequiresReason()
    {
        var (svc, _, _) = CreateInMemorySubscriptionService();
        var tenantId = Guid.NewGuid();
        Assert.True((await svc.StartTrialAsync(tenantId)).Success);
        Assert.True((await svc.CancelAsync(tenantId, immediate: false, reason: "x")).Success);

        var bad = await svc.UndoCancelAtPeriodEndAsync(tenantId, "  ");
        Assert.False(bad.Success);
        Assert.Equal("REASON_REQUIRED", bad.ErrorCode);
    }

    [Fact]
    public async Task UndoCancelAtPeriodEnd_RejectsWhenNotScheduled()
    {
        var (svc, _, _) = CreateInMemorySubscriptionService();
        var tenantId = Guid.NewGuid();
        Assert.True((await svc.StartTrialAsync(tenantId)).Success);

        var bad = await svc.UndoCancelAtPeriodEndAsync(tenantId, "oops");
        Assert.False(bad.Success);
        Assert.Equal("CANCEL_NOT_SCHEDULED", bad.ErrorCode);
    }

    [Fact]
    public async Task UndoCancelAtPeriodEnd_RejectsWhenNoLiveSubscription_IncludingImmediateCancel()
    {
        var (svc, _, _) = CreateInMemorySubscriptionService();
        var tenantId = Guid.NewGuid();
        Assert.True((await svc.StartTrialAsync(tenantId)).Success);
        Assert.True((await svc.CancelAsync(tenantId, immediate: true, reason: "fraud")).Success);

        var bad = await svc.UndoCancelAtPeriodEndAsync(tenantId, "revive");
        Assert.False(bad.Success);
        Assert.Equal("NO_LIVE_SUBSCRIPTION", bad.ErrorCode);
    }

    [Fact]
    public async Task UndoCancelAtPeriodEnd_IsTenantScoped()
    {
        var (svc, db, _) = CreateInMemorySubscriptionService();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.True((await svc.StartTrialAsync(a)).Success);
        Assert.True((await svc.StartTrialAsync(b)).Success);
        Assert.True((await svc.CancelAsync(a, immediate: false, reason: "a")).Success);

        var undoOther = await svc.UndoCancelAtPeriodEndAsync(b, "wrong tenant");
        Assert.False(undoOther.Success);
        Assert.Equal("CANCEL_NOT_SCHEDULED", undoOther.ErrorCode);

        Assert.True((await db.Subscriptions.SingleAsync(s => s.TenantId == a)).CancelAtPeriodEnd);
    }

    private static async Task SeedActiveDueAsync(
        GymFlowProDbContext infra,
        PlatformDbContext platform,
        Guid tenantId,
        DateOnly today,
        int periodEndOffsetDays)
    {
        infra.Tenants.Add(NewTenant(tenantId));
        await infra.SaveChangesAsync();

        var periodEnd = today.AddDays(periodEndOffsetDays);
        platform.Subscriptions.Add(new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = SubscriptionStatuses.Active,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = periodEnd.AddDays(-29),
            CurrentPeriodEnd = periodEnd
        });
        await platform.SaveChangesAsync();
    }

    private static (ISubscriptionService Svc, PlatformDbContext Db, RecordingAudit Audit) CreateInMemorySubscriptionService()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase("p0-lifecycle-" + Guid.NewGuid())
            .Options;
        var db = new PlatformDbContext(options);
        var repo = new SubscriptionWriteRepository(db);
        var memoryCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cache = new SubscriptionStatusCache(memoryCache, NullLogger<SubscriptionStatusCache>.Instance);
        var audit = new RecordingAudit();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformSubscription:TrialDays"] = "14"
            })
            .Build();

        PlatformCommercialPlanTestHelper.SeedCommercialPlansAsync(db).GetAwaiter().GetResult();
        var commercialPlans = PlatformCommercialPlanTestHelper.CreatePlanService(db, audit);

        var svc = new SubscriptionService(
            repo, cache, new AlwaysEnabledFeatureAccess(), new NoopProrationInvoiceService(),
            audit, commercialPlans, config, NullLogger<SubscriptionService>.Instance);
        return (svc, db, audit);
    }

    private static ProcessSubscriptionRenewalsJob CreateRenewalJob(
        PlatformDbContext ctx,
        IPlatformBillingPaymentService payments)
    {
        var repo = new SubscriptionWriteRepository(ctx);
        var cache = new SubscriptionStatusCache(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<SubscriptionStatusCache>.Instance);

        var audit = new NoOpAudit();
        var commercialPlans = PlatformCommercialPlanTestHelper.CreatePlanService(ctx, audit);

        return new ProcessSubscriptionRenewalsJob(
            ctx,
            repo,
            cache,
            audit,
            CreateInvoiceService(ctx),
            payments,
            commercialPlans,
            NullLogger<ProcessSubscriptionRenewalsJob>.Instance);
    }

    private static PlatformInvoiceService CreateInvoiceService(PlatformDbContext ctx)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformBilling:DueDays"] = "7",
                ["PlatformBilling:VatRate"] = "0",
                ["PlatformBilling:LegalName"] = "GymFlow",
                ["PlatformBilling:LegalNameAr"] = "جيم فلو",
                ["PlatformBilling:Code"] = "GYMFLOW"
            })
            .Build();

        return new PlatformInvoiceService(
            ctx,
            new CountingPdfRenderer(),
            new NoOpFileStorageService(),
            new NoopAutomationEnrollment(),
            config,
            NullLogger<PlatformInvoiceService>.Instance);
    }

    private static GymFlowProDbContext CreateInfraDb()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Guid.NewGuid(), "Platform Test", "Africa/Cairo");
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseSqlServer(LocalDbConnectionString)
            .Options;
        return new GymFlowProDbContext(options, tenantContext);
    }

    private static PlatformDbContext CreatePlatformDb()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(LocalDbConnectionString, sql =>
            {
                sql.MigrationsHistoryTable(
                    PlatformServiceExtensions.MigrationsHistoryTable,
                    PlatformServiceExtensions.Schema);
            })
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

    private static Tenant NewTenant(Guid tenantId) => new()
    {
        Id = tenantId,
        Name = "Lifecycle P0 Test Gym",
        NameAr = "اختبار",
        GymCode = $"GYM-{tenantId:N}".Substring(0, 13),
        City = "Cairo",
        Address = "Test",
        PhoneNumber = "+201000000000",
        Email = $"{tenantId:N}@test.local",
        SubscriptionStartDate = DateTime.UtcNow
    };

    private static DateOnly TodayCairo() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));

    private static async Task CleanupAsync(
        PlatformDbContext platform,
        GymFlowProDbContext infra,
        IEnumerable<Guid> tenantIds)
    {
        var ids = tenantIds.ToArray();
        await platform.PlatformInvoices.Where(i => ids.Contains(i.TenantId)).ExecuteDeleteAsync();
        await platform.PlatformPaymentEvents.Where(i => ids.Contains(i.TenantId)).ExecuteDeleteAsync();
        await platform.SubscriptionChanges.Where(c => ids.Contains(c.TenantId)).ExecuteDeleteAsync();
        await platform.Subscriptions.Where(s => ids.Contains(s.TenantId)).ExecuteDeleteAsync();
        await platform.PlatformAuditLogs.Where(a => ids.Contains(a.TenantId ?? Guid.Empty)).ExecuteDeleteAsync();
        await infra.Tenants.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync();
    }

    private sealed class AlwaysPaidPaymentService : IPlatformBillingPaymentService
    {
        public Task<bool> HasPaymentMethodOnFileAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<PlatformPaymentAttemptResult> TryCollectInvoiceAsync(
            PlatformInvoice invoice,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformPaymentAttemptResult
            {
                Success = true,
                PaymentMethod = "test_card",
                PaidAtUtc = DateTime.UtcNow,
                ExternalReference = $"TEST-{invoice.Id:N}"
            });

        public Task<PlatformWebhookProcessResult> HandlePaymobWebhookAsync(
            string rawPayload, string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformWebhookProcessResult { Success = true });

        public Task<PlatformWebhookProcessResult> HandleFawryWebhookAsync(
            string rawPayload, string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformWebhookProcessResult { Success = true });
    }

    private sealed class NeverHasCardPaymentService : IPlatformBillingPaymentService
    {
        public Task<bool> HasPaymentMethodOnFileAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<PlatformPaymentAttemptResult> TryCollectInvoiceAsync(
            PlatformInvoice invoice,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformPaymentAttemptResult
            {
                Success = false,
                FailureCode = "MANUAL_PAYMENT_REQUIRED"
            });

        public Task<PlatformWebhookProcessResult> HandlePaymobWebhookAsync(
            string rawPayload, string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformWebhookProcessResult { Success = true });

        public Task<PlatformWebhookProcessResult> HandleFawryWebhookAsync(
            string rawPayload, string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformWebhookProcessResult { Success = true });
    }

    private sealed class CountingPdfRenderer : IInvoicePdfRenderer
    {
        public byte[] Render(InvoicePdfModel model) => new byte[] { 1, 2, 3, 4 };
    }

    private sealed class NoOpFileStorageService : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) =>
            Task.FromResult($"/uploads/{folder}/{fileName}");
        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(true);
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

    private sealed class NoopAutomationEnrollment : IAutomationEnrollmentService
    {
        public Task<AutomationEnrollment> EnrollAsync(
            string sequenceKey, string subjectType, Guid subjectId, Guid? tenantId,
            DateTime firstRunAtUtc, int initialStep = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AutomationEnrollment
            {
                SequenceKey = sequenceKey,
                SubjectType = subjectType,
                SubjectId = subjectId,
                TenantId = tenantId,
                Step = initialStep,
                NextRunAtUtc = firstRunAtUtc
            });

        public Task<bool> HaltAsync(
            string subjectType, Guid subjectId, string reason, string? sequenceKey = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<AutomationEnrollment?> GetActiveAsync(
            string subjectType, Guid subjectId, string? sequenceKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AutomationEnrollment?>(null);
    }

    private sealed class NoopProrationInvoiceService : IPlatformProrationInvoiceService
    {
        public Task<PlatformInvoice> CreateUpgradeProrationStubAsync(
            Guid tenantId, Guid subscriptionId, decimal proratedAmountEgp,
            string fromTier, string toTier, CancellationToken cancellationToken = default) =>
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

    private sealed class RecordingAudit : IPlatformAuditService
    {
        public List<(Guid Actor, string Action, Guid? TenantId)> Entries { get; } = new();

        public Task LogAsync(
            Guid actorPlatformUserId, string action, Guid? tenantId = null,
            object? before = null, object? after = null, string? ipAddress = null)
        {
            Entries.Add((actorPlatformUserId, action, tenantId));
            return Task.CompletedTask;
        }

        public Task<GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto>> ListAsync(
            Guid? tenantId, string? action, DateOnly? from, DateOnly? to, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto>
            {
                Page = page,
                PageSize = pageSize
            });
    }
}
