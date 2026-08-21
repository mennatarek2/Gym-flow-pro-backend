namespace GMS.Tests.Platform;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Platform;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

/// <summary>
/// CP7: rules-based tenant health scores, graceful degradation, risk queue.
/// Explicitly no ML.
/// </summary>
public class Cp7TenantHealthScoreTests
{
    private const string LocalDb =
        @"Server=(localdb)\mssqllocaldb;Database=GymFlowProDb_PlatformCp7Tests;Trusted_Connection=true;Encrypt=false;";

    [Fact]
    public async Task Score_GracefulDegradation_MissingSignals_StillComputes()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tenantId));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        platform.Subscriptions.Add(NewSub(tenantId, SubscriptionStatuses.Active));
        await platform.SaveChangesAsync();

        var svc = CreateScorer(platform);
        var row = await svc.ScoreTenantAsync(tenantId);

        Assert.InRange(row.Score, 0, 100);
        Assert.True(TenantRiskBands.IsValid(row.RiskBand));
        Assert.False(string.IsNullOrWhiteSpace(row.ContributingFactorsJson));

        using var doc = JsonDocument.Parse(row.ContributingFactorsJson!);
        Assert.Equal("rules_v1", doc.RootElement.GetProperty("model").GetString());
        Assert.False(doc.RootElement.GetProperty("mlUsed").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("confidence").GetDecimal() < 1m);

        var support = doc.RootElement.GetProperty("signals").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == TenantHealthSignals.SupportTicketVolume);
        Assert.False(support.GetProperty("available").GetBoolean());

        await CleanupAsync(platform, tenantId);
    }

    [Fact]
    public async Task Score_PastDueWithUnpaid_LowersPaymentSignal()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tenantId));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        var sub = NewSub(tenantId, SubscriptionStatuses.PastDue);
        platform.Subscriptions.Add(sub);
        platform.PlatformInvoices.Add(new PlatformInvoice
        {
            TenantId = tenantId,
            SubscriptionId = sub.Id,
            InvoiceNumber = $"GFP-H-{Guid.NewGuid():N}"[..18],
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow),
            Subtotal = 1999m,
            VatAmount = 0m,
            Total = 1999m,
            Status = "issued",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3)
        });
        await platform.SaveChangesAsync();

        var row = await CreateScorer(platform).ScoreTenantAsync(tenantId);
        using var doc = JsonDocument.Parse(row.ContributingFactorsJson!);
        var payment = doc.RootElement.GetProperty("signals").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == TenantHealthSignals.PaymentHealth);
        Assert.True(payment.GetProperty("available").GetBoolean());
        Assert.True(payment.GetProperty("score").GetInt32() <= 25);

        await CleanupAsync(platform, tenantId);
    }

    [Fact]
    public async Task NightlyJob_ScoresEveryActiveAndPastDue_SkipsTrialing()
    {
        await EnsureSchemasAsync();
        var activeId = Guid.NewGuid();
        var pastDueId = Guid.NewGuid();
        var trialId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.AddRange(NewTenant(activeId), NewTenant(pastDueId), NewTenant(trialId));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        platform.Subscriptions.AddRange(
            NewSub(activeId, SubscriptionStatuses.Active),
            NewSub(pastDueId, SubscriptionStatuses.PastDue),
            NewSub(trialId, SubscriptionStatuses.Trialing));
        await platform.SaveChangesAsync();

        await CreateScorer(platform).ExecuteAsync();

        Assert.True(await platform.TenantHealthScores.AnyAsync(h => h.TenantId == activeId));
        Assert.True(await platform.TenantHealthScores.AnyAsync(h => h.TenantId == pastDueId));
        Assert.False(await platform.TenantHealthScores.AnyAsync(h => h.TenantId == trialId));

        await CleanupAsync(platform, activeId);
        await CleanupAsync(platform, pastDueId);
        await CleanupAsync(platform, trialId);
    }

    [Fact]
    public async Task RiskQueue_DefaultsToAtRiskAndCritical_SupportsAssignAndOutcome()
    {
        await EnsureSchemasAsync();
        var criticalId = Guid.NewGuid();
        var healthyId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.AddRange(NewTenant(criticalId), NewTenant(healthyId));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        platform.Subscriptions.AddRange(
            NewSub(criticalId, SubscriptionStatuses.Active),
            NewSub(healthyId, SubscriptionStatuses.Active));
        platform.TenantHealthScores.AddRange(
            new TenantHealthScore
            {
                TenantId = criticalId,
                Score = 10,
                RiskBand = TenantRiskBands.Critical,
                ComputedAtUtc = DateTime.UtcNow,
                ContributingFactorsJson = """{"summary":"critical fixture","mlUsed":false}"""
            },
            new TenantHealthScore
            {
                TenantId = healthyId,
                Score = 90,
                RiskBand = TenantRiskBands.Healthy,
                ComputedAtUtc = DateTime.UtcNow
            });
        await platform.SaveChangesAsync();

        var queue = new PlatformRiskQueueService(platform, new RecordingAudit());
        var defaultList = await queue.ListAsync(null);
        Assert.Contains(defaultList, i => i.TenantId == criticalId);
        Assert.DoesNotContain(defaultList, i => i.TenantId == healthyId);

        var assign = await queue.AssignAsync(criticalId, actor, actor, "127.0.0.1");
        Assert.True(assign.Success);

        var outcome = await queue.RecordOutcomeAsync(criticalId, actor, new RecordRiskQueueOutcomeRequest
        {
            Outcome = RiskQueueOutcomes.Contacted,
            Note = "Called owner"
        }, "127.0.0.1");
        Assert.True(outcome.Result.Success);
        Assert.Equal(RiskQueueOutcomes.Contacted, outcome.Outcome!.Outcome);

        var refreshed = await queue.ListAsync("critical");
        var item = Assert.Single(refreshed);
        Assert.Equal(actor, item.AssignedPlatformUserId);
        Assert.Contains(item.RecentOutcomes, o => o.Outcome == RiskQueueOutcomes.Contacted);

        await CleanupAsync(platform, criticalId);
        await CleanupAsync(platform, healthyId);
    }

    [Fact]
    public void Weights_AreConfigurationDriven_NotMagicNumbersInScorerSource()
    {
        var source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "GMS.Platform", "Services", "TenantHealthScoreService.cs"));

        // Defaults may appear as GetValue fallbacks — but must load from PlatformHealth:Weights.
        Assert.Contains("PlatformHealth:Weights", source);
        Assert.Contains("LogInformation", source);
        Assert.Contains("mlUsed", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RandomForest", source);
        Assert.DoesNotContain("Predict(", source);
    }

    private static TenantHealthScoreService CreateScorer(PlatformDbContext platform)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformHealth:Weights:LoginFrequency"] = "0.20",
                ["PlatformHealth:Weights:FeatureBreadth"] = "0.15",
                ["PlatformHealth:Weights:PaymentHealth"] = "0.25",
                ["PlatformHealth:Weights:MemberBaseTrend"] = "0.20",
                ["PlatformHealth:Weights:SupportTicketVolume"] = "0.05",
                ["PlatformHealth:Weights:UsageVsCap"] = "0.15",
                ["PlatformHealth:Bands:HealthyMin"] = "75",
                ["PlatformHealth:Bands:WatchMin"] = "50",
                ["PlatformHealth:Bands:AtRiskMin"] = "25"
            })
            .Build();

        return new TenantHealthScoreService(
            platform, config, NullLogger<TenantHealthScoreService>.Instance);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GMS.Platform", "GMS.Platform.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found.");
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

    private static Tenant NewTenant(Guid id) => new()
    {
        Id = id,
        Name = "CP7 Gym",
        NameAr = "صالة",
        GymCode = $"H7{id.ToString("N")[..6]}",
        City = "Cairo",
        Address = "Test",
        PhoneNumber = "+201000000000",
        Email = $"{id:N}@cp7.test",
        IsActive = true,
        SubscriptionStartDate = DateTime.UtcNow,
        Settings = "{}"
    };

    private static PlatformSubscription NewSub(Guid tenantId, string status) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        PlanTier = PlanTiers.Growth,
        Status = status,
        BillingCycle = BillingCycles.Monthly,
        PriceEgp = 1999m,
        CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
        CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static async Task CleanupAsync(PlatformDbContext platform, Guid tenantId)
    {
        await platform.RiskQueueOutcomes.Where(o => o.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.TenantHealthScores.Where(h => h.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.PlatformInvoices.Where(i => i.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.SubscriptionChanges.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.Subscriptions.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();

        await using var infra = CreateInfraDb();
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

    private sealed class RecordingAudit : IPlatformAuditService
    {
        public Task LogAsync(
            Guid actorPlatformUserId, string action, Guid? tenantId = null,
            object? before = null, object? after = null, string? ipAddress = null) =>
            Task.CompletedTask;
    }
}
