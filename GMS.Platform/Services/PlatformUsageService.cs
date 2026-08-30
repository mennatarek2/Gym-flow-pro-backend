namespace GMS.Platform.Services;

using System.Data;
using Microsoft.EntityFrameworkCore;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

/// <summary>
/// Cross-tenant usage rollup, read entirely from platform.usage_counters (the same table
/// RollUpTenantUsageJob already writes nightly and per-tenant UsageCounters already reads) —
/// no new usage-tracking mechanism, just a read across every tenant instead of one.
/// </summary>
public class PlatformUsageService : IPlatformUsageService
{
    /// <summary>Same "near limit" bar as the Tenant Detail Usage panel's amber threshold (UsagePanel.tsx capTone).</summary>
    private const decimal NearLimitThreshold = 0.8m;

    private readonly PlatformDbContext _db;

    public PlatformUsageService(PlatformDbContext db)
    {
        _db = db;
    }

    public async Task<PlatformUsageSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var period = TierEnforcementService.CurrentPeriodCairo();

        var counters = await _db.UsageCounters
            .AsNoTracking()
            .Where(c => c.Period == period)
            .ToListAsync(cancellationToken);

        var totals = counters
            .GroupBy(c => c.Metric)
            .Select(g => new UsageMetricTotalDto
            {
                Metric = g.Key,
                TotalCount = g.Sum(c => (long)c.Count),
                TenantCount = g.Select(c => c.TenantId).Distinct().Count()
            })
            .OrderBy(t => t.Metric)
            .ToList();

        var nearLimitRows = counters
            .Where(c => c.Cap is > 0 && c.Count >= c.Cap.Value * NearLimitThreshold)
            .ToList();

        var tenantIds = nearLimitRows.Select(c => c.TenantId).Distinct().ToList();
        var tenantNames = await LoadTenantNamesAsync(tenantIds, cancellationToken);

        var nearLimit = nearLimitRows
            .Select(c =>
            {
                tenantNames.TryGetValue(c.TenantId, out var info);
                return new TenantNearLimitDto
                {
                    TenantId = c.TenantId,
                    TenantName = info?.Name ?? string.Empty,
                    GymCode = info?.GymCode ?? string.Empty,
                    Metric = c.Metric,
                    Count = c.Count,
                    Cap = c.Cap!.Value,
                    PercentOfCap = (int)Math.Round((double)c.Count / c.Cap.Value * 100)
                };
            })
            .OrderByDescending(t => t.PercentOfCap)
            .ToList();

        return new PlatformUsageSummaryDto
        {
            Period = period,
            Totals = totals,
            TenantsNearLimit = nearLimit,
            ComputedAtUtc = DateTime.UtcNow
        };
    }

    private sealed record TenantNameRow(string Name, string GymCode);

    /// <summary>Raw SQL against dbo.tenants — PlatformDbContext deliberately has no EF model for
    /// tenant-side tables (mirrors the same lookup already duplicated in PlatformTenantReadService
    /// and PlatformAuditService; kept independent per that established convention, not extracted).</summary>
    private async Task<Dictionary<Guid, TenantNameRow>> LoadTenantNamesAsync(
        List<Guid> tenantIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, TenantNameRow>();
        if (tenantIds.Count == 0 || !_db.Database.IsRelational())
            return result;

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        var paramNames = tenantIds.Select((_, i) => $"@t{i}").ToList();
        command.CommandText = $"SELECT Id, Name, GymCode FROM dbo.tenants WHERE Id IN ({string.Join(",", paramNames)})";
        for (var i = 0; i < tenantIds.Count; i++)
        {
            var param = command.CreateParameter();
            param.ParameterName = paramNames[i];
            param.Value = tenantIds[i];
            command.Parameters.Add(param);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(reader.GetOrdinal("Id"));
            result[id] = new TenantNameRow(
                reader["Name"]?.ToString() ?? string.Empty,
                reader["GymCode"]?.ToString() ?? string.Empty);
        }

        return result;
    }
}
