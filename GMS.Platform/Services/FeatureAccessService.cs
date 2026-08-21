namespace GMS.Platform.Services;

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Platform.Constants;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

/// <summary>
/// Tier map → active overrides → Phase A Tenant.Settings feature_flags deny overlay.
/// Redis-cached; fail-open on cache errors. Missing live subscription → deny modules (no free tier).
/// </summary>
public class FeatureAccessService : IFeatureAccessService
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly PlatformDbContext _db;
    private readonly ISubscriptionWriteRepository _subscriptions;
    private readonly ISubscriptionStatusCache _statusCache;
    private readonly IDistributedCache _cache;
    private readonly ILogger<FeatureAccessService> _logger;

    public FeatureAccessService(
        PlatformDbContext db,
        ISubscriptionWriteRepository subscriptions,
        ISubscriptionStatusCache statusCache,
        IDistributedCache cache,
        ILogger<FeatureAccessService> logger)
    {
        _db = db;
        _subscriptions = subscriptions;
        _statusCache = statusCache;
        _cache = cache;
        _logger = logger;
    }

    public static string BuildCacheKey(Guid tenantId, string featureKey) =>
        $"platform:feature:{tenantId:N}:{featureKey.Trim().ToLowerInvariant()}";

    public async Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default)
    {
        var key = (featureKey ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key))
            return false;

        var cacheKey = BuildCacheKey(tenantId, key);
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is "1" or "0")
                return cached == "1";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feature access cache read failed for {TenantId}/{Feature}", tenantId, key);
        }

        var enabled = await EvaluateAsync(tenantId, key, cancellationToken);

        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                enabled ? "1" : "0",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feature access cache write failed for {TenantId}/{Feature}", tenantId, key);
        }

        return enabled;
    }

    public async Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Known Phase A keys + any override keys for this tenant
        var keys = new HashSet<string>(FeatureKeys.PhaseAModules, StringComparer.OrdinalIgnoreCase);
        var overrideKeys = await _db.FeatureOverrides
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .Select(o => o.FeatureKey)
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var k in overrideKeys)
            keys.Add(k);

        foreach (var featureKey in keys)
        {
            try
            {
                await _cache.RemoveAsync(BuildCacheKey(tenantId, featureKey), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Feature access cache invalidate failed for {TenantId}/{Feature}", tenantId, featureKey);
            }
        }
    }

    private async Task<bool> EvaluateAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken)
    {
        var status = await _statusCache.GetAsync(tenantId, cancellationToken);
        var tier = status?.PlanTier?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(tier))
        {
            var live = await _subscriptions.GetLiveByTenantAsync(tenantId, cancellationToken);
            tier = live?.PlanTier?.Trim().ToLowerInvariant();
        }

        var inTier = false;
        if (!string.IsNullOrEmpty(tier))
        {
            inTier = await _db.TierFeatureMaps
                .AsNoTracking()
                .AnyAsync(m => m.Tier == tier && m.FeatureKey == featureKey, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var overrides = await _db.FeatureOverrides
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId &&
                        o.FeatureKey == featureKey &&
                        (o.ExpiresAtUtc == null || o.ExpiresAtUtc > now))
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var enabled = inTier;
        if (overrides.Count > 0)
            enabled = overrides[0].Enabled;

        // Phase A deny overlay: explicit false disables; missing/true leaves prior result.
        var settingsJson = await LoadTenantSettingsAsync(tenantId, cancellationToken);
        if (!FeatureFlagReader.IsEnabled(settingsJson, featureKey))
            enabled = false;

        return enabled;
    }

    private async Task<string?> LoadTenantSettingsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return null;

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) Settings FROM dbo.tenants WHERE Id = @tenantId";
        var param = command.CreateParameter();
        param.ParameterName = "@tenantId";
        param.Value = tenantId;
        command.Parameters.Add(param);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string ?? result?.ToString();
    }
}
