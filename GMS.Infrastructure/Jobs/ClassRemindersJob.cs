namespace GMS.Infrastructure.Jobs;

using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Runs every 30 minutes (*/30 * * * *).
/// Finds classes starting in ~2 hours and sends WhatsApp reminders.
/// Placeholder — requires a Class/Booking entity which is not yet implemented.
/// </summary>
public class ClassRemindersJob
{
    private readonly ILogger<ClassRemindersJob> _logger;

    public ClassRemindersJob(ILogger<ClassRemindersJob> logger)
    {
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 2)]
    public Task ExecuteAsync()
    {
        _logger.LogInformation(
            "ClassRemindersJob executed at {Time}. " +
            "Class/Booking entities not yet implemented — skipping.",
            DateTime.UtcNow);

        // TODO: When Class + ClassBooking entities are added:
        // 1. Query classes starting between UtcNow+1h50m and UtcNow+2h10m
        // 2. Get booked members for those classes
        // 3. BackgroundJob.Enqueue<IWhatsAppService>(svc => svc.SendClassReminderAsync(...))

        return Task.CompletedTask;
    }
}
