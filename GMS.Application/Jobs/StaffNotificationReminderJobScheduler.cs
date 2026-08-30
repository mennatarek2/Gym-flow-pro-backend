namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>Registers Hangfire recurring job <c>staff-notification-reminders</c> hourly (Cairo).</summary>
public class StaffNotificationReminderJobScheduler : IHostedService
{
    private const string CairoTimeZone = "Egypt Standard Time";
    private readonly ILogger<StaffNotificationReminderJobScheduler> _logger;

    public StaffNotificationReminderJobScheduler(ILogger<StaffNotificationReminderJobScheduler> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById(CairoTimeZone);
        RecurringJob.AddOrUpdate<StaffNotificationReminderJob>(
            "staff-notification-reminders",
            job => job.ExecuteAsync(),
            "15 * * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        _logger.LogInformation(
            "StaffNotificationReminderJobScheduler: staff-notification-reminders registered (hourly :15 Cairo).");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
