namespace GMS.Tests.Platform;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.Members;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Models;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;
using GMS.Platform;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

public class Cp4UsageEnforcementTests
{
    private const string LocalDb =
        @"Server=(localdb)\mssqllocaldb;Database=GymFlowProDb_PlatformCp4Tests;Trusted_Connection=true;Encrypt=false;";

    private static readonly TimeZoneInfo CairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    [Fact]
    public async Task TierFeatureMap_Starter_SeedsBranchesCapOfOne_AndNoBranchesEnforcementFilter()
    {
        await EnsureSchemasAndSeedAsync();
        await using var platform = CreatePlatformDb();

        var row = await platform.TierFeatureMaps
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Tier == PlanTiers.Starter && m.FeatureKey == UsageMetrics.Branches);

        Assert.NotNull(row);
        Assert.Equal(1, row.CapValue);

        // Deferred: no PlanCap action filter type exists for branches yet.
        var planCapTypes = typeof(GMS.Api.Filters.FeatureFlagFilter).Assembly
            .GetTypes()
            .Where(t => t.Name.Contains("PlanCap", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(planCapTypes);
    }

    [Fact]
    public async Task FeatureAccess_StarterOverride_EnablesProOnlyModule_UntilExpired()
    {
        await EnsureSchemasAndSeedAsync();
        var tenantId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tenantId, settings: null));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        platform.Subscriptions.Add(NewSub(tenantId, PlanTiers.Starter));
        await platform.SaveChangesAsync();

        // Imports is Growth+ only (TierFeatureMapSeed) — genuinely absent from Starter's tier map,
        // unlike Refunds which is baseline for every tier ("retail POS must be able to reverse a
        // cash sale") and so is never a meaningful example of a tier-exclusive override anymore.
        var access = CreateFeatureAccess(platform);
        Assert.False(await access.IsEnabledAsync(tenantId, FeatureKeys.Imports));

        platform.FeatureOverrides.Add(new FeatureOverride
        {
            TenantId = tenantId,
            FeatureKey = FeatureKeys.Imports,
            Enabled = true,
            Reason = "test grant",
            GrantedByPlatformUserId = Guid.NewGuid(),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });
        await platform.SaveChangesAsync();
        await access.InvalidateAsync(tenantId);
        Assert.True(await access.IsEnabledAsync(tenantId, FeatureKeys.Imports));

        var ov = await platform.FeatureOverrides.FirstAsync(o => o.TenantId == tenantId);
        ov.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await platform.SaveChangesAsync();
        await access.InvalidateAsync(tenantId);
        Assert.False(await access.IsEnabledAsync(tenantId, FeatureKeys.Imports));

        await CleanupAsync(platform, tenantId);
    }

    [Fact]
    public async Task FeatureAccess_PhaseA_JsonDeny_StillDisablesSales()
    {
        await EnsureSchemasAndSeedAsync();
        var tenantId = Guid.NewGuid();
        var settings = JsonSerializer.Serialize(new { feature_flags = new { sales = false } });

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tenantId, settings));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        platform.Subscriptions.Add(NewSub(tenantId, PlanTiers.Pro));
        await platform.SaveChangesAsync();

        var access = CreateFeatureAccess(platform);
        Assert.False(await access.IsEnabledAsync(tenantId, FeatureKeys.Sales));
        Assert.True(await access.IsEnabledAsync(tenantId, FeatureKeys.Shifts));

        await CleanupAsync(platform, tenantId);
    }

    [Fact]
    public async Task StaffSeats_HardBlock_ReturnsPlanLimitExceeded_BeforeIdentityWrite()
    {
        await EnsureSchemasAndSeedAsync();
        var tenantId = Guid.NewGuid();

        await using var infra = CreateInfraDb();
        infra.Tenants.Add(NewTenant(tenantId, null));
        await infra.SaveChangesAsync();

        var admin = new AdminService(
            infra,
            userManager: null!,
            NullLogger<AdminService>.Instance,
            new NoopPermissionCache(),
            new BlockingStaffSeatsEnforcement(),
            new NoOpAudit(),
            new NoopFiles());

        var result = await admin.CreateStaffUserAsync(tenantId, new CreateStaffRequest
        {
            FullName = "Blocked Seat",
            Email = $"blocked-{tenantId:N}@test.local",
            Password = "Passw0rd!",
            Role = "trainer"
        });

        Assert.False(result.IsSuccess);
        Assert.StartsWith("PLAN_LIMIT_EXCEEDED|", result.Error);
        Assert.Empty(infra.Users.Where(u => u.TenantId == tenantId));

        await using var platform = CreatePlatformDb();
        await CleanupAsync(platform, tenantId);
    }

    [Fact]
    public async Task MemberSoftCap_SurfacesWarning_DoesNotBlock()
    {
        await EnsureSchemasAndSeedAsync();
        var tenantId = Guid.NewGuid();

        await using var infra = CreateInfraDb();
        infra.Tenants.Add(NewTenant(tenantId, null));
        await infra.SaveChangesAsync();

        var members = new MemberService(
            infra,
            new MemberRepository(infra),
            new AesEncryptionService(new ConfigurationBuilder().Build()),
            new SoftMemberCapEnforcement(cap: 10, count: 10),
            new NoOpReferralAttribution(),
            new NoOpMemberAppActivation(),
            new ActivityEntitlementService(infra),
            NullLogger<MemberService>.Instance);

        var result = await members.CreateMemberAsync(tenantId, new CreateMemberRequest
        {
            FullName = "Soft Cap",
            FullNameAr = "حد مرن",
            Phone = "+201011112233",
            DateOfBirth = new DateOnly(1990, 1, 1)
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("PLAN_SOFT_CAP:active_members", result.Message);

        await using var platform = CreatePlatformDb();
        await CleanupAsync(platform, tenantId);
    }

    [Fact]
    public async Task RollUp_Idempotent_AndWhatsAppOverageMath()
    {
        await EnsureSchemasAndSeedAsync();
        var tenantId = Guid.NewGuid();
        var period = TierEnforcementService.CurrentPeriodCairo();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tenantId, null));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        platform.Subscriptions.Add(NewSub(tenantId, PlanTiers.Starter));
        await platform.SaveChangesAsync();

        var enforcement = new FixedCountsEnforcement(new Dictionary<string, (int Count, int? Cap)>
        {
            [UsageMetrics.ActiveMembers] = (50, 200),
            [UsageMetrics.StaffSeats] = (2, 3),
            [UsageMetrics.Branches] = (1, 1),
            [UsageMetrics.WhatsAppMessages] = (600, 500)
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformBilling:WhatsAppOverageEgpPerMessage"] = "0.35"
            })
            .Build();

        var job = new RollUpTenantUsageJob(
            platform, enforcement, config, NullLogger<RollUpTenantUsageJob>.Instance);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        var counters = await platform.UsageCounters
            .Where(c => c.TenantId == tenantId && c.Period == period)
            .ToListAsync();

        Assert.Equal(4, counters.Count);
        Assert.Equal(4, counters.Select(c => c.Metric).Distinct().Count());

        var wa = counters.Single(c => c.Metric == UsageMetrics.WhatsAppMessages);
        Assert.Equal(600, wa.Count);
        Assert.Equal(500, wa.Cap);
        Assert.Equal(35.00m, wa.OverageBilledEgp);

        await CleanupAsync(platform, tenantId);
    }

    [Fact]
    public async Task RenewalInvoice_IncludesWhatsAppOverageLine_FromPriorPeriod()
    {
        await EnsureSchemasAndSeedAsync();
        var tenantId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tenantId, null));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTz));
        var sub = NewSub(tenantId, PlanTiers.Growth);
        sub.PriceEgp = 1999m;
        platform.Subscriptions.Add(sub);

        var priorDay = today.AddDays(-1);
        var priorPeriod = $"{priorDay.Year:D4}-{priorDay.Month:D2}";
        platform.UsageCounters.Add(new UsageCounter
        {
            TenantId = tenantId,
            Period = priorPeriod,
            Metric = UsageMetrics.WhatsAppMessages,
            Count = 2100,
            Cap = 2000,
            OverageBilledEgp = 35m
        });
        await platform.SaveChangesAsync();

        var invoices = CreateInvoiceService(platform);
        var invoice = await invoices.EnsureRenewalInvoiceAsync(sub, today, today.AddMonths(1));

        Assert.False(string.IsNullOrWhiteSpace(invoice.LinesSnapshot));
        var lines = JsonSerializer.Deserialize<List<InvoicePdfLineModel>>(invoice.LinesSnapshot!);
        Assert.NotNull(lines);
        Assert.Equal(2, lines.Count);
        Assert.Equal(2034m, invoice.Subtotal);
        Assert.Contains(lines, l => l.Description.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase));

        await CleanupAsync(platform, tenantId);
    }

    private static async Task EnsureSchemasAndSeedAsync()
    {
        await using var infra = CreateInfraDb();
        await infra.Database.MigrateAsync();

        await using var platform = CreatePlatformDb();
        await platform.Database.MigrateAsync();

        var expected = TierFeatureMapSeed.BuildAll();
        var existing = await platform.TierFeatureMaps.Select(m => m.Tier + "|" + m.FeatureKey).ToListAsync();
        var set = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = expected.Where(r => !set.Contains($"{r.Tier}|{r.FeatureKey}")).ToList();
        if (missing.Count > 0)
        {
            platform.TierFeatureMaps.AddRange(missing);
            await platform.SaveChangesAsync();
        }
    }

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
            .UseSqlServer(LocalDb, sql =>
            {
                sql.MigrationsHistoryTable(
                    PlatformServiceExtensions.MigrationsHistoryTable,
                    PlatformServiceExtensions.Schema);
            })
            .Options;
        return new PlatformDbContext(options);
    }

    private static FeatureAccessService CreateFeatureAccess(PlatformDbContext platform)
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var statusCache = new SubscriptionStatusCache(cache, NullLogger<SubscriptionStatusCache>.Instance);
        var repo = new SubscriptionWriteRepository(platform);
        return new FeatureAccessService(
            platform, repo, statusCache, cache, NullLogger<FeatureAccessService>.Instance);
    }

    private static PlatformInvoiceService CreateInvoiceService(PlatformDbContext platform)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformBilling:DueDays"] = "7",
                ["PlatformBilling:VatRate"] = "0"
            })
            .Build();

        return new PlatformInvoiceService(
            platform,
            new CountingPdfRenderer(),
            new NoopFileStorage(),
            new NoopAutomationEnrollment(),
            config,
            NullLogger<PlatformInvoiceService>.Instance);
    }

    private static Tenant NewTenant(Guid tenantId, string? settings) => new()
    {
        Id = tenantId,
        Name = "CP4 Test Gym",
        NameAr = "اختبار",
        GymCode = $"GYM-{tenantId:N}".Substring(0, 13),
        City = "Cairo",
        Address = "Test",
        PhoneNumber = "+201000000000",
        Email = $"{tenantId:N}@test.local",
        SubscriptionStartDate = DateTime.UtcNow,
        Settings = string.IsNullOrWhiteSpace(settings) ? "{}" : settings
    };

    private static PlatformSubscription NewSub(Guid tenantId, string tier)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTz));
        return new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = tier,
            Status = SubscriptionStatuses.Active,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 999m,
            CurrentPeriodStart = today.AddDays(-10),
            CurrentPeriodEnd = today.AddDays(20)
        };
    }

    private static async Task CleanupAsync(PlatformDbContext platform, Guid tenantId)
    {
        await platform.UsageCounters.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.FeatureOverrides.Where(o => o.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.PlatformInvoices.Where(i => i.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.SubscriptionChanges.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.Subscriptions.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();

        await using var infra = CreateInfraDb();
        await infra.GymMembers.IgnoreQueryFilters().Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
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

    private sealed class NoopPermissionCache : IPermissionCacheService
    {
        public Task<IReadOnlySet<string>?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>?>(null);

        public Task SetAsync(Guid tenantId, Guid userId, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private sealed class NoopFiles : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) =>
            Task.FromResult($"/uploads/{folder}/{fileName}");
        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(false);
    }

    private sealed class BlockingStaffSeatsEnforcement : ITierEnforcementService
    {
        public Task<CapCheckResult> CheckCapAsync(Guid tenantId, string metric, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapCheckResult
            {
                Allowed = false,
                SoftWarning = false,
                Count = 3,
                Cap = 3,
                Metric = UsageMetrics.StaffSeats
            });
    }

    private sealed class SoftMemberCapEnforcement : ITierEnforcementService
    {
        private readonly int _cap;
        private readonly int _count;

        public SoftMemberCapEnforcement(int cap, int count)
        {
            _cap = cap;
            _count = count;
        }

        public Task<CapCheckResult> CheckCapAsync(Guid tenantId, string metric, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapCheckResult
            {
                Allowed = true,
                SoftWarning = _count >= _cap,
                Count = _count,
                Cap = _cap,
                Metric = metric
            });
    }

    private sealed class FixedCountsEnforcement : ITierEnforcementService
    {
        private readonly Dictionary<string, (int Count, int? Cap)> _map;

        public FixedCountsEnforcement(Dictionary<string, (int Count, int? Cap)> map) => _map = map;

        public Task<CapCheckResult> CheckCapAsync(Guid tenantId, string metric, CancellationToken cancellationToken = default)
        {
            var (count, cap) = _map[metric];
            return Task.FromResult(new CapCheckResult
            {
                Allowed = true,
                SoftWarning = false,
                Count = count,
                Cap = cap,
                Metric = metric
            });
        }
    }

    private sealed class CountingPdfRenderer : IInvoicePdfRenderer
    {
        public byte[] Render(InvoicePdfModel model) => new byte[] { 1, 2, 3 };
    }

    private sealed class NoopFileStorage : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) =>
            Task.FromResult($"/uploads/{folder}/{fileName}");

        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;

        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(true);
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
}
