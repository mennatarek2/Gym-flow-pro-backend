namespace GMS.Platform.Services;

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Platform.Constants;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

/// <summary>
/// Live usage vs tier caps. Counts come from dbo (members / AspNetUsers / notifications).
/// branches returns 1 until a Branch module exists — callers must not hard-block on it in CP4.
/// </summary>
public class TierEnforcementService : ITierEnforcementService
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeZoneInfo CairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly PlatformDbContext _db;
    private readonly ISubscriptionWriteRepository _subscriptions;
    private readonly ISubscriptionStatusCache _statusCache;
    private readonly IDistributedCache _cache;
    private readonly ILogger<TierEnforcementService> _logger;

    public TierEnforcementService(
        PlatformDbContext db,
        ISubscriptionWriteRepository subscriptions,
        ISubscriptionStatusCache statusCache,
        IDistributedCache cache,
        ILogger<TierEnforcementService> logger)
    {
        _db = db;
        _subscriptions = subscriptions;
        _statusCache = statusCache;
        _cache = cache;
        _logger = logger;
    }

    public static string BuildCacheKey(Guid tenantId, string metric) =>
        $"platform:cap:{tenantId:N}:{metric.Trim().ToLowerInvariant()}";

    public async Task<CapCheckResult> CheckCapAsync(
        Guid tenantId,
        string metric,
        CancellationToken cancellationToken = default)
    {
        var key = (metric ?? string.Empty).Trim().ToLowerInvariant();
        if (!UsageMetrics.All.Contains(key))
        {
            return new CapCheckResult
            {
                Allowed = true,
                SoftWarning = false,
                Count = 0,
                Cap = null,
                Metric = key
            };
        }

        var cacheKey = BuildCacheKey(tenantId, key);
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (!string.IsNullOrEmpty(cached) && TryParseCached(cached, out var fromCache))
                return fromCache;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cap cache read failed for {TenantId}/{Metric}", tenantId, key);
        }

        var result = await EvaluateAsync(tenantId, key, cancellationToken);

        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                Serialize(result),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cap cache write failed for {TenantId}/{Metric}", tenantId, key);
        }

        return result;
    }

    private async Task<CapCheckResult> EvaluateAsync(Guid tenantId, string metric, CancellationToken cancellationToken)
    {
        var status = await _statusCache.GetAsync(tenantId, cancellationToken);
        var tier = status?.PlanTier?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(tier))
        {
            var live = await _subscriptions.GetLiveByTenantAsync(tenantId, cancellationToken);
            tier = live?.PlanTier?.Trim().ToLowerInvariant();
        }

        int? cap = null;
        if (!string.IsNullOrEmpty(tier))
        {
            var map = await _db.TierFeatureMaps
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Tier == tier && m.FeatureKey == metric, cancellationToken);
            cap = map?.CapValue;
        }

        var count = await CountAsync(tenantId, metric, cancellationToken);

        return metric switch
        {
            UsageMetrics.StaffSeats => new CapCheckResult
            {
                Metric = metric,
                Count = count,
                Cap = cap,
                Allowed = cap is null || count < cap.Value,
                SoftWarning = false
            },
            UsageMetrics.ActiveMembers => new CapCheckResult
            {
                Metric = metric,
                Count = count,
                Cap = cap,
                Allowed = true,
                SoftWarning = cap is not null && count >= cap.Value
            },
            UsageMetrics.WhatsAppMessages => new CapCheckResult
            {
                Metric = metric,
                Count = count,
                Cap = cap,
                Allowed = true,
                SoftWarning = cap is not null && count >= cap.Value
            },
            UsageMetrics.Branches => new CapCheckResult
            {
                Metric = metric,
                Count = count,
                Cap = cap,
                Allowed = true,
                SoftWarning = false
            },
            _ => new CapCheckResult
            {
                Metric = metric,
                Count = count,
                Cap = cap,
                Allowed = true,
                SoftWarning = false
            }
        };
    }

    private async Task<int> CountAsync(Guid tenantId, string metric, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
        {
            // InMemory tests: use usage_counters if present, else 0 (branches → 1).
            if (metric == UsageMetrics.Branches)
                return 1;

            var period = CurrentPeriodCairo();
            var row = await _db.UsageCounters
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    c => c.TenantId == tenantId && c.Period == period && c.Metric == metric,
                    cancellationToken);
            return row?.Count ?? 0;
        }

        return metric switch
        {
            UsageMetrics.ActiveMembers => await ExecuteCountAsync(
                """
                SELECT COUNT(1)
                FROM dbo.gym_members
                WHERE TenantId = @tenantId AND IsDeleted = 0
                """,
                tenantId,
                cancellationToken),
            UsageMetrics.StaffSeats => await ExecuteCountAsync(
                """
                SELECT COUNT(1)
                FROM AspNetUsers u
                WHERE u.TenantId = @tenantId
                  AND u.IsActive = 1
                  AND EXISTS (
                      SELECT 1
                      FROM AspNetUserRoles ur
                      INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
                      WHERE ur.UserId = u.Id AND r.Name <> N'Member'
                  )
                """,
                tenantId,
                cancellationToken),
            UsageMetrics.WhatsAppMessages => await ExecuteWhatsAppMonthCountAsync(tenantId, cancellationToken),
            UsageMetrics.Branches => 1, // single-tenant gym today; Branch CRUD deferred
            _ => 0
        };
    }

    private async Task<int> ExecuteWhatsAppMonthCountAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // Source of truth: successful outbound WhatsApp rows in dbo.notifications for the Cairo month.
        // Platform / ops WhatsApp (billing reminders) may also land here when TenantId is set.
        var nowCairo = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTz);
        var monthStart = new DateTime(nowCairo.Year, nowCairo.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var monthEnd = monthStart.AddMonths(1);
        var monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(monthStart, CairoTz);
        var monthEndUtc = TimeZoneInfo.ConvertTimeToUtc(monthEnd, CairoTz);

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM dbo.notifications
            WHERE TenantId = @tenantId
              AND IsDeleted = 0
              AND Channel = N'whatsapp'
              AND Status IN (N'sent', N'delivered')
              AND SentAtUtc >= @monthStartUtc AND SentAtUtc < @monthEndUtc
            """;

        AddGuid(command, "@tenantId", tenantId);
        AddDateTime(command, "@monthStartUtc", monthStartUtc);
        AddDateTime(command, "@monthEndUtc", monthEndUtc);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(scalar ?? 0);
    }

    private async Task<int> ExecuteCountAsync(string sql, Guid tenantId, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddGuid(command, "@tenantId", tenantId);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(scalar ?? 0);
    }

    public static string CurrentPeriodCairo()
    {
        var cairo = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTz);
        return $"{cairo:yyyy-MM}";
    }

    private static void AddGuid(IDbCommand command, string name, Guid value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        command.Parameters.Add(p);
    }

    private static void AddDateTime(IDbCommand command, string name, DateTime value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        command.Parameters.Add(p);
    }

    private static string Serialize(CapCheckResult r) =>
        $"{(r.Allowed ? 1 : 0)}|{(r.SoftWarning ? 1 : 0)}|{r.Count}|{(r.Cap?.ToString() ?? "")}|{r.Metric}";

    private static bool TryParseCached(string cached, out CapCheckResult result)
    {
        result = new CapCheckResult();
        var parts = cached.Split('|');
        if (parts.Length != 5)
            return false;

        int? cap = string.IsNullOrEmpty(parts[3]) ? null : int.Parse(parts[3]);
        result = new CapCheckResult
        {
            Allowed = parts[0] == "1",
            SoftWarning = parts[1] == "1",
            Count = int.Parse(parts[2]),
            Cap = cap,
            Metric = parts[4]
        };
        return true;
    }
}
