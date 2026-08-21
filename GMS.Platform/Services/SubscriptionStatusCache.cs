namespace GMS.Platform.Services;

using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>
/// Redis-backed subscription status cache for GetStatusAsync hot path.
/// Fail-open: Redis errors never break reads (fall through to DB).
/// </summary>
public class SubscriptionStatusCache : ISubscriptionStatusCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IDistributedCache _cache;
    private readonly ILogger<SubscriptionStatusCache> _logger;

    public SubscriptionStatusCache(IDistributedCache cache, ILogger<SubscriptionStatusCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public static string BuildKey(Guid tenantId) => $"platform:sub:status:{tenantId:N}";

    public async Task<SubscriptionStatusDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _cache.GetStringAsync(BuildKey(tenantId), cancellationToken);
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonSerializer.Deserialize<SubscriptionStatusDto>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscription status cache read failed for tenant {TenantId}", tenantId);
            return null;
        }
    }

    public async Task SetAsync(Guid tenantId, SubscriptionStatusDto status, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(status, JsonOptions);
            await _cache.SetStringAsync(
                BuildKey(tenantId),
                json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscription status cache write failed for tenant {TenantId}", tenantId);
        }
    }

    public async Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(BuildKey(tenantId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscription status cache invalidation failed for tenant {TenantId}", tenantId);
        }
    }
}
