namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Platform.Constants;

/// <summary>
/// Daily low-stock + batch-expiry staff alerts (INVS-10). Lives in Application because it calls
/// <see cref="IInventoryReportService"/> — same pattern as <see cref="ZReportGenerationJob"/>.
/// </summary>
public class InventoryLowStockJob
{
    private static readonly TimeZoneInfo CairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InventoryLowStockJob> _logger;

    public InventoryLowStockJob(IServiceScopeFactory scopeFactory, ILogger<InventoryLowStockJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var featureAccess = scope.ServiceProvider.GetRequiredService<IFeatureAccessService>();
        var reports = scope.ServiceProvider.GetRequiredService<IInventoryReportService>();

        var cairoDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTz));

        var tenants = await db.Tenants
            .Where(t => t.IsActive && !t.IsDeleted)
            .Select(t => new { t.Id, t.Name, t.TimeZone })
            .ToListAsync();

        _logger.LogInformation(
            "InventoryLowStockJob: scanning {Count} tenants for {Date}",
            tenants.Count, cairoDate);

        var processed = 0;
        foreach (var tenant in tenants)
        {
            try
            {
                if (!await featureAccess.IsEnabledAsync(tenant.Id, FeatureKeys.Inventory))
                    continue;

                tenantContext.SetTenant(tenant.Id, tenant.Name, tenant.TimeZone);
                var result = await reports.RunDailyAlertsAsync(tenant.Id, cairoDate);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "InventoryLowStockJob: tenant {TenantId} failed: {Error}",
                        tenant.Id, result.Error);
                    continue;
                }

                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InventoryLowStockJob: tenant {TenantId}", tenant.Id);
            }
        }

        _logger.LogInformation(
            "InventoryLowStockJob: completed — {Processed}/{Total} inventory-enabled tenants",
            processed, tenants.Count);
    }
}
