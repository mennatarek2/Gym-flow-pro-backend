namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>Registers Hangfire recurring job <c>inventory-low-stock</c> at 08:00 Cairo (INVS-10).</summary>
public class InventoryLowStockJobScheduler : IHostedService
{
    private const string CairoTimeZone = "Egypt Standard Time";
    private readonly ILogger<InventoryLowStockJobScheduler> _logger;

    public InventoryLowStockJobScheduler(ILogger<InventoryLowStockJobScheduler> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById(CairoTimeZone);

        RecurringJob.AddOrUpdate<InventoryLowStockJob>(
            "inventory-low-stock",
            job => job.ExecuteAsync(),
            "0 8 * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        _logger.LogInformation(
            "InventoryLowStockJobScheduler: inventory-low-stock registered (08:00 Cairo).");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
