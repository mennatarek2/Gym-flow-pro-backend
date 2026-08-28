namespace GMS.Tests.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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

/// <summary>P1 production hardening — StartTrial domain rule, HasPaymentMethodOnFile scoping.</summary>
public class PlatformProductionHardeningTests
{
    private const string LocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb_PlatformHardeningTests;Trusted_Connection=true;Encrypt=false;";

    private static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(SubscriptionStatuses.Active)]
    [InlineData(SubscriptionStatuses.Trialing)]
    [InlineData(SubscriptionStatuses.PastDue)]
    public async Task StartTrial_RejectsWhenLiveSubscriptionExists(string status)
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            platform.Subscriptions.Add(new PlatformSubscription
            {
                TenantId = tenantId,
                PlanTier = PlanTiers.Growth,
                Status = status,
                BillingCycle = BillingCycles.Monthly,
                PriceEgp = 1999m,
                CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(29))
            });
            await platform.SaveChangesAsync();

            var svc = CreateSubscriptionService(platform);
            var result = await svc.StartTrialAsync(tenantId, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId);
            Assert.False(result.Success);
            Assert.Equal("LIVE_SUBSCRIPTION_EXISTS", result.ErrorCode);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task StartTrial_RejectsWhenSuspendedSubscriptionExists()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            platform.Subscriptions.Add(new PlatformSubscription
            {
                TenantId = tenantId,
                PlanTier = PlanTiers.Growth,
                Status = SubscriptionStatuses.Suspended,
                BillingCycle = BillingCycles.Monthly,
                PriceEgp = 1999m,
                CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                SuspendedAtUtc = DateTime.UtcNow.AddDays(-1)
            });
            await platform.SaveChangesAsync();

            var svc = CreateSubscriptionService(platform);
            var result = await svc.StartTrialAsync(tenantId, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId);
            Assert.False(result.Success);
            Assert.Equal("NON_CANCELLED_SUBSCRIPTION_EXISTS", result.ErrorCode);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task StartTrial_AllowedAfterCancelledSubscription()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, tenantId);
        try
        {
            platform.Subscriptions.Add(new PlatformSubscription
            {
                TenantId = tenantId,
                PlanTier = PlanTiers.Growth,
                Status = SubscriptionStatuses.Cancelled,
                BillingCycle = BillingCycles.Monthly,
                PriceEgp = 1999m,
                CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)),
                CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                CancelledAtUtc = DateTime.UtcNow.AddDays(-30)
            });
            await platform.SaveChangesAsync();

            var svc = CreateSubscriptionService(platform);
            var result = await svc.StartTrialAsync(tenantId, PlanTiers.Starter, SubscriptionInitiators.PlatformAdmin, ActorId, 30);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, await platform.Subscriptions.CountAsync(s => s.TenantId == tenantId));
            Assert.Equal(
                SubscriptionStatuses.Trialing,
                await platform.Subscriptions
                    .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatuses.Trialing)
                    .Select(s => s.Status)
                    .SingleAsync());
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task StartTrial_AllowedWhenNoSubscription()
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
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task StartTrial_DoesNotAffectOtherTenant()
    {
        await EnsureSchemasAsync();
        var blocked = Guid.NewGuid();
        var allowed = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        await SeedTenantAsync(infra, blocked);
        await SeedTenantAsync(infra, allowed);
        try
        {
            platform.Subscriptions.Add(new PlatformSubscription
            {
                TenantId = blocked,
                PlanTier = PlanTiers.Growth,
                Status = SubscriptionStatuses.Suspended,
                BillingCycle = BillingCycles.Monthly,
                PriceEgp = 1999m,
                CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20))
            });
            await platform.SaveChangesAsync();

            var svc = CreateSubscriptionService(platform);
            var blockedResult = await svc.StartTrialAsync(blocked, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId);
            var allowedResult = await svc.StartTrialAsync(allowed, PlanTiers.Growth, SubscriptionInitiators.PlatformAdmin, ActorId);

            Assert.False(blockedResult.Success);
            Assert.True(allowedResult.Success);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { blocked, allowed });
        }
    }

    [Theory]
    [InlineData(SubscriptionStatuses.Active, true)]
    [InlineData(SubscriptionStatuses.Active, false)]
    [InlineData(SubscriptionStatuses.Trialing, true)]
    [InlineData(SubscriptionStatuses.Trialing, false)]
    public async Task HasPaymentMethodOnFile_UsesLiveSubscriptionOnly(string status, bool withToken)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PlatformDbContext(options);
        var tenantId = Guid.NewGuid();

        db.Subscriptions.Add(new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = status,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            SavedCardToken = withToken ? "tok_live" : null
        });
        await db.SaveChangesAsync();

        var svc = CreatePaymentService(db);
        var has = await svc.HasPaymentMethodOnFileAsync(tenantId);
        Assert.Equal(withToken, has);
    }

    [Fact]
    public async Task HasPaymentMethodOnFile_IgnoresCancelledTokenWhenNoLiveSub()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PlatformDbContext(options);
        var tenantId = Guid.NewGuid();

        db.Subscriptions.Add(new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = SubscriptionStatuses.Cancelled,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)),
            CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            SavedCardToken = "stale_tok"
        });
        await db.SaveChangesAsync();

        var svc = CreatePaymentService(db);
        Assert.False(await svc.HasPaymentMethodOnFileAsync(tenantId));
    }

    [Fact]
    public async Task HasPaymentMethodOnFile_OldCancelledToken_NewActiveWithoutToken_IsFalse()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PlatformDbContext(options);
        var tenantId = Guid.NewGuid();

        db.Subscriptions.AddRange(
            new PlatformSubscription
            {
                TenantId = tenantId,
                PlanTier = PlanTiers.Growth,
                Status = SubscriptionStatuses.Cancelled,
                BillingCycle = BillingCycles.Monthly,
                PriceEgp = 1999m,
                CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)),
                CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)),
                SavedCardToken = "old_tok"
            },
            new PlatformSubscription
            {
                TenantId = tenantId,
                PlanTier = PlanTiers.Growth,
                Status = SubscriptionStatuses.Active,
                BillingCycle = BillingCycles.Monthly,
                PriceEgp = 1999m,
                CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(25)),
                SavedCardToken = null
            });
        await db.SaveChangesAsync();

        var svc = CreatePaymentService(db);
        Assert.False(await svc.HasPaymentMethodOnFileAsync(tenantId));
    }

    [Fact]
    public async Task HasPaymentMethodOnFile_OldCancelledToken_NewActiveWithToken_IsTrue()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PlatformDbContext(options);
        var tenantId = Guid.NewGuid();

        db.Subscriptions.AddRange(
            new PlatformSubscription
            {
                TenantId = tenantId,
                PlanTier = PlanTiers.Growth,
                Status = SubscriptionStatuses.Cancelled,
                BillingCycle = BillingCycles.Monthly,
                PriceEgp = 1999m,
                CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)),
                CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)),
                SavedCardToken = "old_tok"
            },
            new PlatformSubscription
            {
                TenantId = tenantId,
                PlanTier = PlanTiers.Growth,
                Status = SubscriptionStatuses.Active,
                BillingCycle = BillingCycles.Monthly,
                PriceEgp = 1999m,
                CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(25)),
                SavedCardToken = "new_tok"
            });
        await db.SaveChangesAsync();

        var svc = CreatePaymentService(db);
        Assert.True(await svc.HasPaymentMethodOnFileAsync(tenantId));
    }

    private static PlatformBillingPaymentService CreatePaymentService(PlatformDbContext db)
    {
        var config = new ConfigurationBuilder().Build();
        using var http = new HttpClient();
        return new PlatformBillingPaymentService(
            db,
            new SubscriptionStatusCache(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                NullLogger<SubscriptionStatusCache>.Instance),
            new NoopAutomationEnrollment(),
            new NoOpAudit(),
            new NoopWhatsApp(),
            new PlatformMerchantPaymobService(http, config, FakeDevEnvironment(), NullLogger<PlatformMerchantPaymobService>.Instance),
            new PlatformMerchantFawryService(http, config, FakeDevEnvironment(), NullLogger<PlatformMerchantFawryService>.Instance),
            config,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<PlatformBillingPaymentService>.Instance);
    }

    private static IHostEnvironment FakeDevEnvironment() =>
        new HostEnvironment { EnvironmentName = Environments.Development };

    private sealed class HostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static SubscriptionService CreateSubscriptionService(PlatformDbContext platform)
    {
        var repo = new SubscriptionWriteRepository(platform);
        var cache = new SubscriptionStatusCache(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<SubscriptionStatusCache>.Instance);
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
        tenantContext.SetTenant(Guid.NewGuid(), "Hardening Test", "Africa/Cairo");
        return new GymFlowProDbContext(
            new DbContextOptionsBuilder<GymFlowProDbContext>().UseSqlServer(LocalDbConnectionString).Options,
            tenantContext);
    }

    private static PlatformDbContext CreatePlatformDb() =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(LocalDbConnectionString, sql =>
                sql.MigrationsHistoryTable(PlatformServiceExtensions.MigrationsHistoryTable, PlatformServiceExtensions.Schema))
            .Options);

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
            Name = "Hardening Test Gym",
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

    private sealed class NoopWhatsApp : IWhatsAppService
    {
        public Task SendExpiryReminderAsync(Guid memberId, int daysLeft) => Task.CompletedTask;
        public Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(Guid memberId, string discountCode) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode) => Task.CompletedTask;
        public Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime) => Task.CompletedTask;
        public Task SendClassReminderAsync(string phone, string className, DateTime startTime) => Task.CompletedTask;
        public Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate) => Task.CompletedTask;
        public Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry) => Task.CompletedTask;
        public Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters) => Task.CompletedTask;
        public Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr) =>
            Task.CompletedTask;
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
