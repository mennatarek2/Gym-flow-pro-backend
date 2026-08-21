namespace GMS.Infrastructure.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Runs daily at 00:05 Cairo. Flips active/frozen memberships with EndDate &lt; today to expired
/// so list KPIs, filters, and check-in search match door eligibility.
/// </summary>
public class MembershipStatusExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MembershipStatusExpiryJob> _logger;

    public MembershipStatusExpiryJob(
        IServiceScopeFactory scopeFactory,
        ILogger<MembershipStatusExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();

        var today = MembershipOperational.TodayCairo();

        var stale = await dbContext.Memberships
            .IgnoreQueryFilters()
            .Where(m => !m.IsDeleted
                     && (m.Status == "active" || m.Status == "frozen")
                     && m.EndDate < today)
            .ToListAsync();

        var marked = 0;
        foreach (var membership in stale)
        {
            if (MembershipOperational.TryMarkExpired(membership, today))
                marked++;
        }

        if (marked > 0)
            await dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "MembershipStatusExpiryJob: marked {Count} membership(s) expired (Cairo today {Today})",
            marked, today);
    }
}
