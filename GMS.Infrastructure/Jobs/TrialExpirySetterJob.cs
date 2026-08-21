namespace GMS.Infrastructure.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Runs daily at midnight Cairo time. Safety-net sweep: any trial membership past its EndDate whose
/// GymMember is still marked 'active_trial' gets flipped to 'expired'.
/// </summary>
public class TrialExpirySetterJob
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrialExpirySetterJob> _logger;

    public TrialExpirySetterJob(IServiceScopeFactory scopeFactory, ILogger<TrialExpirySetterJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();
        var featureAccess = scope.ServiceProvider.GetRequiredService<IFeatureAccessService>();

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));

        // Feature-flag no-op via IFeatureAccessService (tier + override + JSON deny).
        var activeTenantIds = await dbContext.Tenants.IgnoreQueryFilters()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync();

        var tenantsWithTrialsDisabled = new HashSet<Guid>();
        foreach (var tenantId in activeTenantIds)
        {
            if (!await featureAccess.IsEnabledAsync(tenantId, "trials"))
                tenantsWithTrialsDisabled.Add(tenantId);
        }

        var expiredTrials = await dbContext.Memberships
            .IgnoreQueryFilters()
            .Include(m => m.Member)
            .Include(m => m.Plan)
            .Where(m => m.Plan!.PlanType == "trial"
                     && m.EndDate < today
                     && m.Member!.TrialOutcome == "active_trial"
                     && !m.Member.IsDeleted
                     && !tenantsWithTrialsDisabled.Contains(m.TenantId))
            .ToListAsync();

        foreach (var membership in expiredTrials)
        {
            membership.Member!.TrialOutcome = "expired";
            membership.Member.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (expiredTrials.Count > 0)
            await dbContext.SaveChangesAsync();

        _logger.LogInformation("TrialExpirySetterJob: marked {Count} trial(s) as expired", expiredTrials.Count);
    }
}
