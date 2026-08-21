namespace GMS.Infrastructure.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Runs daily at 7:00 AM Cairo time (0 7 * * *).
/// Queries memberships expiring in 7, 3, 1, and 0 days.
/// Enqueues one WhatsApp child job per member with exponential backoff retry.
/// </summary>
public class MembershipExpiryNotificationsJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MembershipExpiryNotificationsJob> _logger;

    public MembershipExpiryNotificationsJob(
        IServiceScopeFactory scopeFactory,
        ILogger<MembershipExpiryNotificationsJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        _logger.LogInformation("MembershipExpiryNotificationsJob started at {Time}", DateTime.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var targetDates = new[]
        {
            (Date: today, DaysLeft: 0),
            (Date: today.AddDays(1), DaysLeft: 1),
            (Date: today.AddDays(3), DaysLeft: 3),
            (Date: today.AddDays(7), DaysLeft: 7)
        };

        var endDates = targetDates.Select(t => t.Date).ToList();

        // Query all active memberships expiring on target dates, grouped by tenant
        var expiringMemberships = await dbContext.Memberships
            .IgnoreQueryFilters()
            .Where(m => m.Status == "active" && endDates.Contains(m.EndDate) && !m.IsDeleted)
            .Select(m => new { m.Id, m.MemberId, m.EndDate, m.TenantId })
            .ToListAsync();

        _logger.LogInformation(
            "Found {Count} expiring memberships across {Dates} target dates",
            expiringMemberships.Count, endDates.Count);

        var enqueued = 0;
        foreach (var membership in expiringMemberships)
        {
            var daysLeft = targetDates.First(t => t.Date == membership.EndDate).DaysLeft;

            // Enqueue child job for each member (Hangfire manages retries)
            BackgroundJob.Enqueue<IWhatsAppService>(
                svc => svc.SendExpiryReminderAsync(membership.MemberId, daysLeft));

            enqueued++;
        }

        _logger.LogInformation(
            "MembershipExpiryNotificationsJob completed. Enqueued {Count} WhatsApp notifications.",
            enqueued);
    }
}
