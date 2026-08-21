namespace GMS.Infrastructure.Jobs;

using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Runs on the 1st of every month at midnight (0 0 1 * *).
/// Generates PDF payout summary per trainer.
/// Placeholder — requires Trainer entity with commission tracking.
/// </summary>
public class TrainerCommissionReportJob
{
    private readonly ILogger<TrainerCommissionReportJob> _logger;

    public TrainerCommissionReportJob(ILogger<TrainerCommissionReportJob> logger)
    {
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public Task ExecuteAsync()
    {
        var reportPeriod = DateTime.UtcNow.AddMonths(-1).ToString("yyyy-MM");

        _logger.LogInformation(
            "TrainerCommissionReportJob: Generating reports for period {Period}. " +
            "Trainer/Commission entities not yet implemented — skipping.",
            reportPeriod);

        // TODO: When Trainer + TrainerSession entities are added:
        // 1. Query completed PT sessions per trainer for last month
        // 2. Calculate commission per rate tier
        // 3. Generate PDF using a reporting library
        // 4. Store PDF in Azure Blob Storage
        // 5. Notify gym owner via WhatsApp/email

        return Task.CompletedTask;
    }
}
