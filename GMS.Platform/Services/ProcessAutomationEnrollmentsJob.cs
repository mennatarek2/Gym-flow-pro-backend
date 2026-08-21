namespace GMS.Platform.Services;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

/// <summary>
/// Polls due automation enrollments and dispatches to the matching <see cref="IAutomationSequenceHandler"/>.
/// Halt is event-driven via <see cref="IAutomationEnrollmentService.HaltAsync"/> — this job only advances schedule.
/// </summary>
public class ProcessAutomationEnrollmentsJob : IProcessAutomationEnrollmentsJob
{
    private readonly PlatformDbContext _db;
    private readonly IEnumerable<IAutomationSequenceHandler> _handlers;
    private readonly ILogger<ProcessAutomationEnrollmentsJob> _logger;

    public ProcessAutomationEnrollmentsJob(
        PlatformDbContext db,
        IEnumerable<IAutomationSequenceHandler> handlers,
        ILogger<ProcessAutomationEnrollmentsJob> logger)
    {
        _db = db;
        _handlers = handlers;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var due = await _db.AutomationEnrollments
            .Where(e => e.HaltedReason == null && e.NextRunAtUtc <= now)
            .OrderBy(e => e.NextRunAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
            return;

        var byKey = _handlers.ToDictionary(h => h.SequenceKey, StringComparer.OrdinalIgnoreCase);

        foreach (var enrollment in due)
        {
            try
            {
                if (!byKey.TryGetValue(enrollment.SequenceKey, out var handler))
                {
                    _logger.LogWarning(
                        "No handler for sequence {SequenceKey}; leaving enrollment {Id} queued.",
                        enrollment.SequenceKey, enrollment.Id);
                    continue;
                }

                // Re-check halt (payment may have won the race).
                await _db.Entry(enrollment).ReloadAsync(cancellationToken);
                if (enrollment.HaltedReason != null)
                    continue;

                var result = await handler.ExecuteStepAsync(enrollment, cancellationToken);

                await _db.Entry(enrollment).ReloadAsync(cancellationToken);
                if (enrollment.HaltedReason != null)
                    continue;

                if (result.Halt || result.NextStep is null || result.NextRunAtUtc is null)
                {
                    enrollment.HaltedReason = result.HaltReason ?? "completed";
                    enrollment.HaltedAtUtc = DateTime.UtcNow;
                    enrollment.UpdatedAtUtc = DateTime.UtcNow;
                }
                else
                {
                    enrollment.Step = result.NextStep.Value;
                    enrollment.NextRunAtUtc = result.NextRunAtUtc.Value;
                    enrollment.UpdatedAtUtc = DateTime.UtcNow;
                }

                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automation enrollment {Id} step failed", enrollment.Id);
            }
        }
    }
}

public class PlatformAutomationJobScheduler : IHostedService
{
    private readonly ILogger<PlatformAutomationJobScheduler> _logger;

    public PlatformAutomationJobScheduler(ILogger<PlatformAutomationJobScheduler> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Every minute so T+N steps fire near schedule; halt itself is webhook-driven.
        RecurringJob.AddOrUpdate<IProcessAutomationEnrollmentsJob>(
            "platform-automation-enrollments",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Minutely);

        _logger.LogInformation("PlatformAutomationJobScheduler: enrollment runner registered (every minute).");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
