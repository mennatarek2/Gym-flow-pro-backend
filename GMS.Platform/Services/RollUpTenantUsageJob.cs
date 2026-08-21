namespace GMS.Platform.Services;

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

/// <summary>
/// Nightly upsert of platform.usage_counters for the current Cairo YYYY-MM period.
/// WhatsApp overage_billed_egp = max(0, count - cap) * PlatformBilling:WhatsAppOverageEgpPerMessage when cap is set.
/// </summary>
public class RollUpTenantUsageJob : IRollUpTenantUsageJob
{
    private static readonly TimeZoneInfo CairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly PlatformDbContext _db;
    private readonly ITierEnforcementService _enforcement;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RollUpTenantUsageJob> _logger;

    public RollUpTenantUsageJob(
        PlatformDbContext db,
        ITierEnforcementService enforcement,
        IConfiguration configuration,
        ILogger<RollUpTenantUsageJob> logger)
    {
        _db = db;
        _enforcement = enforcement;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var period = TierEnforcementService.CurrentPeriodCairo();
        var rate = _configuration.GetValue("PlatformBilling:WhatsAppOverageEgpPerMessage", 0.35m);

        var liveStatuses = SubscriptionStatuses.Live.ToList();
        var tenantIds = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => liveStatuses.Contains(s.Status))
            .Select(s => s.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "RollUpTenantUsage: rolling up {Count} live tenant(s) for period {Period}",
            tenantIds.Count, period);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                await RollUpTenantAsync(tenantId, period, rate, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RollUpTenantUsage failed for tenant {TenantId}", tenantId);
            }
        }
    }

    private async Task RollUpTenantAsync(
        Guid tenantId,
        string period,
        decimal whatsAppRate,
        CancellationToken cancellationToken)
    {
        foreach (var metric in UsageMetrics.All)
        {
            var check = await _enforcement.CheckCapAsync(tenantId, metric, cancellationToken);
            decimal? overage = null;
            if (metric == UsageMetrics.WhatsAppMessages && check.Cap is int cap)
            {
                var extra = Math.Max(0, check.Count - cap);
                overage = Math.Round(extra * whatsAppRate, 2, MidpointRounding.AwayFromZero);
            }

            var existing = await _db.UsageCounters
                .FirstOrDefaultAsync(
                    c => c.TenantId == tenantId && c.Period == period && c.Metric == metric,
                    cancellationToken);

            if (existing is null)
            {
                _db.UsageCounters.Add(new UsageCounter
                {
                    TenantId = tenantId,
                    Period = period,
                    Metric = metric,
                    Count = check.Count,
                    Cap = check.Cap,
                    OverageBilledEgp = overage,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.Count = check.Count;
                existing.Cap = check.Cap;
                existing.OverageBilledEgp = overage;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}

public class PlatformUsageJobScheduler : IHostedService
{
    private readonly ILogger<PlatformUsageJobScheduler> _logger;

    public PlatformUsageJobScheduler(ILogger<PlatformUsageJobScheduler> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        Hangfire.RecurringJob.AddOrUpdate<IRollUpTenantUsageJob>(
            "platform-usage-rollup",
            job => job.ExecuteAsync(CancellationToken.None),
            "30 1 * * *",
            new Hangfire.RecurringJobOptions { TimeZone = cairoTz });

        _logger.LogInformation("PlatformUsageJobScheduler: recurring usage rollup registered (01:30 Cairo).");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
