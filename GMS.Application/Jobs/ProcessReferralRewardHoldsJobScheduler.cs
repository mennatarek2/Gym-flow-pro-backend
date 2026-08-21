namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>Registers hourly referral hold processor (Application layer — IReferralRewardService).</summary>
public class ProcessReferralRewardHoldsJobScheduler : IHostedService
{
    private const string CairoTimeZone = "Egypt Standard Time";
    private readonly ILogger<ProcessReferralRewardHoldsJobScheduler> _logger;

    public ProcessReferralRewardHoldsJobScheduler(ILogger<ProcessReferralRewardHoldsJobScheduler> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById(CairoTimeZone);

        RecurringJob.AddOrUpdate<ProcessReferralRewardHoldsJob>(
            "referral-reward-holds",
            job => job.ExecuteAsync(),
            "0 * * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        _logger.LogInformation(
            "ProcessReferralRewardHoldsJobScheduler: referral-reward-holds registered (hourly Cairo).");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
