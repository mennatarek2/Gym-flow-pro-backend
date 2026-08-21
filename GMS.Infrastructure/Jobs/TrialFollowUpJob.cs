namespace GMS.Infrastructure.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Runs daily at 9:00 AM Cairo time. Sends trial-expiry WhatsApp nudges: "last day" reminders for
/// trials expiring today, and a follow-up offer for trials that expired exactly 2 days ago and
/// haven't already been marked converted/expired.
/// </summary>
public class TrialFollowUpJob
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrialFollowUpJob> _logger;

    public TrialFollowUpJob(IServiceScopeFactory scopeFactory, ILogger<TrialFollowUpJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();
        var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
        var featureAccess = scope.ServiceProvider.GetRequiredService<IFeatureAccessService>();

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));

        await SendLastDayRemindersAsync(dbContext, whatsAppService, featureAccess, today);
        await SendFollowUpOffersAsync(dbContext, whatsAppService, featureAccess, today);
    }

    private async Task SendLastDayRemindersAsync(
        GymFlowProDbContext dbContext,
        IWhatsAppService whatsAppService,
        IFeatureAccessService featureAccess,
        DateOnly today)
    {
        var tenantsWithTrialsDisabled = await GetTenantsWithTrialsDisabledAsync(dbContext, featureAccess);

        var expiringToday = await dbContext.Memberships
            .IgnoreQueryFilters()
            .Include(m => m.Member)
            .Include(m => m.Plan)
            .Where(m => m.Plan!.PlanType == "trial"
                     && m.EndDate == today
                     && m.Member!.TrialOutcome == "active_trial"
                     && !m.Member.IsDeleted
                     && !tenantsWithTrialsDisabled.Contains(m.TenantId))
            .ToListAsync();

        foreach (var membership in expiringToday)
        {
            if (string.IsNullOrWhiteSpace(membership.Member?.PhoneNumber))
                continue;

            await whatsAppService.SendTemplateAsync(membership.Member.PhoneNumber, "trial_last_day", new Dictionary<string, string>
            {
                ["memberName"] = membership.Member.FullName,
                ["planName"] = membership.Plan?.Name ?? string.Empty
            });
        }

        _logger.LogInformation("TrialFollowUpJob: sent {Count} last-day reminder(s)", expiringToday.Count);
    }

    private async Task SendFollowUpOffersAsync(
        GymFlowProDbContext dbContext,
        IWhatsAppService whatsAppService,
        IFeatureAccessService featureAccess,
        DateOnly today)
    {
        var twoDaysAgo = today.AddDays(-2);
        var tenantsWithTrialsDisabled = await GetTenantsWithTrialsDisabledAsync(dbContext, featureAccess);

        var expiredTwoDaysAgo = await dbContext.Memberships
            .IgnoreQueryFilters()
            .Include(m => m.Member)
            .Include(m => m.Plan)
            .Where(m => m.Plan!.PlanType == "trial"
                     && m.EndDate == twoDaysAgo
                     && m.Member!.TrialOutcome == "active_trial"
                     && !m.Member.IsDeleted
                     && !tenantsWithTrialsDisabled.Contains(m.TenantId))
            .ToListAsync();

        foreach (var membership in expiredTwoDaysAgo)
        {
            if (membership.Member == null)
                continue;

            if (!string.IsNullOrWhiteSpace(membership.Member.PhoneNumber))
            {
                await whatsAppService.SendTemplateAsync(membership.Member.PhoneNumber, "trial_followup_offer", new Dictionary<string, string>
                {
                    ["memberName"] = membership.Member.FullName,
                    ["planName"] = membership.Plan?.Name ?? string.Empty
                });
            }

            membership.Member.TrialOutcome = "expired";
            membership.Member.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (expiredTwoDaysAgo.Count > 0)
            await dbContext.SaveChangesAsync();

        _logger.LogInformation("TrialFollowUpJob: sent {Count} follow-up offer(s) and marked them expired", expiredTwoDaysAgo.Count);
    }

    /// <summary>Feature-flag no-op: tenants with the "trials" module disabled are skipped entirely —
    /// the job itself is never removed from the schedule (see FeatureFlagFilter's doc comment).</summary>
    private static async Task<HashSet<Guid>> GetTenantsWithTrialsDisabledAsync(
        GymFlowProDbContext dbContext,
        IFeatureAccessService featureAccess)
    {
        var tenantIds = await dbContext.Tenants.IgnoreQueryFilters()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync();

        var disabled = new HashSet<Guid>();
        foreach (var tenantId in tenantIds)
        {
            if (!await featureAccess.IsEnabledAsync(tenantId, "trials"))
                disabled.Add(tenantId);
        }

        return disabled;
    }
}
