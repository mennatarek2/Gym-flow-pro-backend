namespace GMS.Infrastructure.Jobs;

using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// IHostedService that registers all Hangfire recurring jobs on app startup.
/// All cron times are in Egypt timezone (Africa/Cairo, UTC+2).
/// </summary>
public class JobScheduler : IHostedService
{
    private readonly ILogger<JobScheduler> _logger;
    private const string CairoTimeZone = "Egypt Standard Time"; // Windows TZ ID for Africa/Cairo

    public JobScheduler(ILogger<JobScheduler> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("JobScheduler: Registering recurring jobs (Cairo timezone)...");

        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById(CairoTimeZone);

        // 1. Membership Expiry Notifications — 7:00 AM Cairo daily
        RecurringJob.AddOrUpdate<MembershipExpiryNotificationsJob>(
            "membership-expiry-notifications",
            job => job.ExecuteAsync(),
            "0 7 * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        // 2. Birthday Greetings — 6:00 AM Cairo daily
        RecurringJob.AddOrUpdate<BirthdayGreetingsJob>(
            "birthday-greetings",
            job => job.ExecuteAsync(),
            "0 6 * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        // 3. Analytics Aggregation — Midnight Cairo daily
        RecurringJob.AddOrUpdate<AnalyticsAggregationJob>(
            "analytics-aggregation",
            job => job.ExecuteAsync(),
            "0 0 * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        // 4. Class Reminders — Every 30 minutes
        RecurringJob.AddOrUpdate<ClassRemindersJob>(
            "class-reminders",
            job => job.ExecuteAsync(),
            "*/30 * * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        // 5. Invitation Quota Reset — 1st of month at midnight
        RecurringJob.AddOrUpdate<InvitationQuotaResetJob>(
            "invitation-quota-reset",
            job => job.ExecuteAsync(),
            "0 0 1 * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        // 6. Trainer Commission Report — 1st of month at midnight
        RecurringJob.AddOrUpdate<TrainerCommissionReportJob>(
            "trainer-commission-report",
            job => job.ExecuteAsync(),
            "0 0 1 * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        // 8. Trial Follow-Up — 9:00 AM Cairo daily
        RecurringJob.AddOrUpdate<TrialFollowUpJob>(
            "trial-followup",
            job => job.ExecuteAsync(),
            "0 9 * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        // 9. Trial Expiry Setter — Midnight Cairo daily
        RecurringJob.AddOrUpdate<TrialExpirySetterJob>(
            "trial-expiry-setter",
            job => job.ExecuteAsync(),
            "0 0 * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        // 9b. Membership Status Expiry — 00:05 Cairo daily (after midnight trial sweep)
        RecurringJob.AddOrUpdate<MembershipStatusExpiryJob>(
            "membership-status-expiry",
            job => job.ExecuteAsync(),
            "5 0 * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        // 10. Daily Digest — 9:00 AM Cairo daily
        RecurringJob.AddOrUpdate<DailyDigestJob>(
            "daily-digest",
            job => job.ExecuteAsync(),
            "0 9 * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        _logger.LogInformation("JobScheduler: All recurring jobs registered successfully (job #7, z-report-generation, is registered separately by ZReportJobScheduler).");

        // Catch up past-end memberships immediately on boot (does not wait for 00:05 cron).
        BackgroundJob.Enqueue<MembershipStatusExpiryJob>(job => job.ExecuteAsync());

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("JobScheduler: Stopping.");
        return Task.CompletedTask;
    }
}
