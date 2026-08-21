namespace GMS.Infrastructure.Services;

using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;

/// <summary>
/// Redis-backed (via <see cref="IDistributedCache"/>) permission cache.
/// Cache reads/writes are best-effort: if Redis is unreachable we log and fall back to
/// recomputing permissions from <see cref="IPermissionProvider"/> rather than failing login.
/// </summary>
public class RedisPermissionCacheService : IPermissionCacheService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisPermissionCacheService> _logger;

    public RedisPermissionCacheService(IDistributedCache cache, ILogger<RedisPermissionCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlySet<string>?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _cache.GetStringAsync(BuildKey(tenantId, userId), cancellationToken);
            if (string.IsNullOrEmpty(json))
                return null;

            var permissions = JsonSerializer.Deserialize<string[]>(json);
            return permissions is null ? null : new HashSet<string>(permissions, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Permission cache read failed for user {UserId} — falling back to recompute.", userId);
            return null;
        }
    }

    public async Task SetAsync(Guid tenantId, Guid userId, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(permissions);
            await _cache.SetStringAsync(
                BuildKey(tenantId, userId),
                json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Permission cache write failed for user {UserId}.", userId);
        }
    }

    public async Task InvalidateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(BuildKey(tenantId, userId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Permission cache invalidation failed for user {UserId}.", userId);
        }
    }

    private static string BuildKey(Guid tenantId, Guid userId) => $"perm:{tenantId}:{userId}";
}
