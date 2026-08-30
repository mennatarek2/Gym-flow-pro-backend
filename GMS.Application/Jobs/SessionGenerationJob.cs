namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Hourly rolling-window generation of class sessions from recurring schedules, plus
/// finalization (complete elapsed sessions, mark un-checked-in booked members as no-show).
/// Application-layer job — same pattern as ZReportGenerationJob (Application interface deps).
/// </summary>
public class SessionGenerationJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionGenerationJob> _logger;

    public SessionGenerationJob(IServiceScopeFactory scopeFactory, ILogger<SessionGenerationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var generator = scope.ServiceProvider.GetRequiredService<ISessionGenerationService>();

        // Tenant entity has no global query filter (see GymFlowProDbContext) — safe pre-context.
        var tenants = await dbContext.Tenants
            .Where(t => t.IsActive && !t.IsDeleted)
            .Select(t => new { t.Id, t.Name })
            .ToListAsync();

        foreach (var tenant in tenants)
        {
            try
            {
                // Hangfire jobs calling services must ITenantContext.SetTenant first (bug-090 pattern).
                tenantContext.SetTenant(tenant.Id, tenant.Name, "Africa/Cairo");
                var created = await generator.GenerateUpcomingSessionsAsync(tenant.Id);
                var noShows = await generator.FinalizeElapsedSessionsAsync(tenant.Id);
                if (created > 0 || noShows > 0)
                    _logger.LogInformation(
                        "SessionGenerationJob tenant {TenantId}: created {Created} sessions, marked {NoShows} no-shows",
                        tenant.Id, created, noShows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SessionGenerationJob failed for tenant {TenantId}", tenant.Id);
            }
            finally
            {
                tenantContext.Clear();
            }
        }
    }
}

public class SessionGenerationJobScheduler : IHostedService
{
    private const string CairoTimeZone = "Egypt Standard Time";
    private readonly ILogger<SessionGenerationJobScheduler> _logger;

    public SessionGenerationJobScheduler(ILogger<SessionGenerationJobScheduler> logger) => _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById(CairoTimeZone);
        RecurringJob.AddOrUpdate<SessionGenerationJob>(
            "activity-session-generation",
            job => job.ExecuteAsync(),
            "5 * * * *", // hourly at :05
            new RecurringJobOptions { TimeZone = cairoTz });
        _logger.LogInformation("SessionGenerationJobScheduler: activity-session-generation registered (hourly).");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
