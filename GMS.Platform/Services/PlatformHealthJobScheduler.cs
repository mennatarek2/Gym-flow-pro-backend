namespace GMS.Platform.Services;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GMS.Platform.Interfaces;

/// <summary>
/// Registers Hangfire recurring job for nightly tenant health scoring (03:00 Cairo —
/// after usage rollup 01:30 and renewals 02:00 so counters/invoices are fresh).
/// </summary>
public class PlatformHealthJobScheduler : IHostedService
{
    private readonly ILogger<PlatformHealthJobScheduler> _logger;

    public PlatformHealthJobScheduler(ILogger<PlatformHealthJobScheduler> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        Hangfire.RecurringJob.AddOrUpdate<IComputeTenantHealthScoresJob>(
            "platform-tenant-health-scores",
            job => job.ExecuteAsync(CancellationToken.None),
            "0 3 * * *",
            new Hangfire.RecurringJobOptions { TimeZone = cairoTz });

        _logger.LogInformation(
            "PlatformHealthJobScheduler: ComputeTenantHealthScores registered (03:00 Cairo, rules-based / no ML).");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
