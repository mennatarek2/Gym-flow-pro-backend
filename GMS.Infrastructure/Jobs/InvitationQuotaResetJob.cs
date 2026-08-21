namespace GMS.Infrastructure.Jobs;

using Hangfire;
using Microsoft.Extensions.Logging;
using GMS.Core.Utilities;

/// <summary>
/// Runs on the 1st of every month at midnight (0 0 1 * *).
/// No-op reset: guest quota is period-count of <c>VisitedAtUtc</c> rows for Cairo <c>yyyy-MM</c>
/// vs plan.InvitationQuota. A new month automatically starts remaining at full plan allowance.
/// This job only logs the period rollover for audit trail.
/// </summary>
public class InvitationQuotaResetJob
{
    private readonly ILogger<InvitationQuotaResetJob> _logger;

    public InvitationQuotaResetJob(ILogger<InvitationQuotaResetJob> logger)
    {
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 1)]
    public Task ExecuteAsync()
    {
        var newPeriod = MembershipOperational.TodayCairo().ToString("yyyy-MM");

        _logger.LogInformation(
            "InvitationQuotaResetJob: New Cairo quota period started → {Period}. " +
            "No denormalized reset — usage = visited guest_pass count for that period.",
            newPeriod);

        return Task.CompletedTask;
    }
}
