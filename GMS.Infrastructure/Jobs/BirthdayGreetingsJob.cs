namespace GMS.Infrastructure.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Runs daily at 6:00 AM Cairo time (0 6 * * *).
/// Finds members with today's birthday and sends WhatsApp greeting with discount code.
/// </summary>
public class BirthdayGreetingsJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BirthdayGreetingsJob> _logger;

    public BirthdayGreetingsJob(
        IServiceScopeFactory scopeFactory,
        ILogger<BirthdayGreetingsJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        _logger.LogInformation("BirthdayGreetingsJob started at {Time}", DateTime.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Find members whose birthday is today (match month + day, any year)
        var birthdayMembers = await dbContext.GymMembers
            .IgnoreQueryFilters()
            .Where(m => m.DateOfBirth.Month == today.Month
                     && m.DateOfBirth.Day == today.Day
                     && m.IsActive
                     && !m.IsDeleted)
            .Select(m => new { m.Id, m.FullName })
            .ToListAsync();

        _logger.LogInformation("Found {Count} birthday members today", birthdayMembers.Count);

        foreach (var member in birthdayMembers)
        {
            // Generate a simple discount code: BDAY-{MemberIdShort}-{MMYY}
            var discountCode = $"BDAY-{member.Id.ToString()[..8].ToUpper()}-{today:MMyy}";

            BackgroundJob.Enqueue<IWhatsAppService>(
                svc => svc.SendBirthdayGreetingAsync(member.Id, discountCode));
        }

        _logger.LogInformation(
            "BirthdayGreetingsJob completed. Sent {Count} birthday greetings.",
            birthdayMembers.Count);
    }
}
