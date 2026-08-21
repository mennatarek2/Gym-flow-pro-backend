namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Registers the Z-Report recurring job (#7 in the overall job list). Lives in GMS.Application
/// rather than alongside the other 6 recurring jobs in GMS.Infrastructure.Jobs.JobScheduler because
/// <see cref="ZReportGenerationJob"/> depends on IZReportService, an Application-layer interface —
/// GMS.Infrastructure does not reference GMS.Application (see ZReportGenerationJob's doc comment).
/// </summary>
public class ZReportJobScheduler : IHostedService
{
    private const string CairoTimeZone = "Egypt Standard Time";
    private readonly ILogger<ZReportJobScheduler> _logger;

    public ZReportJobScheduler(ILogger<ZReportJobScheduler> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById(CairoTimeZone);

        // 7. Z-Report Generation — 23:59 Cairo daily
        RecurringJob.AddOrUpdate<ZReportGenerationJob>(
            "z-report-generation",
            job => job.ExecuteAsync(),
            "59 23 * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        _logger.LogInformation("ZReportJobScheduler: recurring job #7 (z-report-generation) registered.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
