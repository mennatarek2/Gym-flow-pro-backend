namespace GMS.Infrastructure.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Runs daily at midnight Cairo time (0 0 * * *).
/// Pre-computes KPIs into gym_analytics_snapshots table per tenant.
/// Uses raw SQL for performance (not LINQ).
/// </summary>
public class AnalyticsAggregationJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalyticsAggregationJob> _logger;

    public AnalyticsAggregationJob(
        IServiceScopeFactory scopeFactory,
        ILogger<AnalyticsAggregationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        _logger.LogInformation("AnalyticsAggregationJob started at {Time}", DateTime.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayDt = DateTime.UtcNow.Date;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthStartDt = monthStart.ToDateTime(TimeOnly.MinValue);
        var quotaPeriod = today.ToString("yyyy-MM");

        // Get all active tenants
        var tenantIds = await dbContext.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.IsActive && !t.IsDeleted)
            .Select(t => new { t.Id, t.Currency })
            .ToListAsync();

        _logger.LogInformation("Aggregating analytics for {Count} tenants", tenantIds.Count);

        foreach (var tenant in tenantIds)
        {
            try
            {
                // Use raw SQL MERGE (UPSERT) for performance
                await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                    MERGE INTO gym_analytics_snapshots AS target
                    USING (
                        SELECT
                            {tenant.Id} AS TenantId,
                            {today} AS SnapshotDate,
                            (SELECT COUNT(*) FROM memberships WHERE TenantId = {tenant.Id} AND Status = 'active' AND IsDeleted = 0) AS ActiveMembers,
                            (SELECT COUNT(*) FROM memberships WHERE TenantId = {tenant.Id} AND Status = 'expired' AND IsDeleted = 0) AS ExpiredMembers,
                            (SELECT COUNT(*) FROM memberships WHERE TenantId = {tenant.Id} AND Status = 'frozen' AND IsDeleted = 0) AS FrozenMembers,
                            (SELECT COUNT(*) FROM gym_members WHERE TenantId = {tenant.Id} AND CreatedAtUtc >= {monthStartDt} AND IsDeleted = 0) AS NewMembersThisMonth,
                            (SELECT ISNULL(SUM(AmountPaid), 0) FROM memberships WHERE TenantId = {tenant.Id} AND PaymentDate >= {monthStartDt} AND IsDeleted = 0) AS RevenueThisMonth,
                            (SELECT COUNT(*) FROM gym_attendance WHERE TenantId = {tenant.Id} AND CheckInAtUtc >= {todayDt} AND IsDeleted = 0) AS CheckinsToday,
                            (SELECT COUNT(*) FROM gym_attendance WHERE TenantId = {tenant.Id} AND CheckInAtUtc >= {monthStartDt} AND IsDeleted = 0) AS CheckinsThisMonth,
                            (SELECT COUNT(*) FROM member_invitations WHERE TenantId = {tenant.Id} AND QuotaPeriod = {quotaPeriod} AND IsDeleted = 0) AS InvitationsSentThisMonth,
                            (SELECT COUNT(*) FROM member_invitations WHERE TenantId = {tenant.Id} AND QuotaPeriod = {quotaPeriod} AND Status = 'converted' AND IsDeleted = 0) AS InvitationsConvertedThisMonth
                    ) AS source
                    ON target.TenantId = source.TenantId AND target.SnapshotDate = source.SnapshotDate
                    WHEN MATCHED THEN
                        UPDATE SET
                            ActiveMembers = source.ActiveMembers,
                            ExpiredMembers = source.ExpiredMembers,
                            FrozenMembers = source.FrozenMembers,
                            NewMembersThisMonth = source.NewMembersThisMonth,
                            RevenueThisMonth = source.RevenueThisMonth,
                            CheckinsToday = source.CheckinsToday,
                            CheckinsThisMonth = source.CheckinsThisMonth,
                            InvitationsSentThisMonth = source.InvitationsSentThisMonth,
                            InvitationsConvertedThisMonth = source.InvitationsConvertedThisMonth
                    WHEN NOT MATCHED THEN
                        INSERT (Id, TenantId, SnapshotDate, ActiveMembers, ExpiredMembers, FrozenMembers,
                                NewMembersThisMonth, RevenueThisMonth, Currency,
                                CheckinsToday, CheckinsThisMonth,
                                InvitationsSentThisMonth, InvitationsConvertedThisMonth, IsDeleted)
                        VALUES (NEWID(), source.TenantId, source.SnapshotDate,
                                source.ActiveMembers, source.ExpiredMembers, source.FrozenMembers,
                                source.NewMembersThisMonth, source.RevenueThisMonth, {tenant.Currency},
                                source.CheckinsToday, source.CheckinsThisMonth,
                                source.InvitationsSentThisMonth, source.InvitationsConvertedThisMonth, 0);
                ");

                _logger.LogDebug("Analytics snapshot updated for tenant {TenantId}", tenant.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to aggregate analytics for tenant {TenantId}", tenant.Id);
            }
        }

        _logger.LogInformation(
            "AnalyticsAggregationJob completed for {Count} tenants.",
            tenantIds.Count);
    }
}
