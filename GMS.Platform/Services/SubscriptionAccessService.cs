namespace GMS.Platform.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Platform.Constants;
using GMS.Platform.Persistence;

/// <summary>
/// Tenant-facing subscription access for auth middleware / login (includes suspended).
/// Fail-open on cache errors; suspended rows are SoT in platform.subscriptions.
/// </summary>
public class SubscriptionAccessService : ISubscriptionAccessService
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private readonly PlatformDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly ILogger<SubscriptionAccessService> _logger;

    public SubscriptionAccessService(
        PlatformDbContext db,
        IDistributedCache cache,
        ILogger<SubscriptionAccessService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public static string BuildKey(Guid tenantId) => $"platform:sub:access:{tenantId:N}";

    public async Task<SubscriptionAccessSnapshot?> GetAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cached = await _cache.GetStringAsync(BuildKey(tenantId), cancellationToken);
            if (!string.IsNullOrEmpty(cached))
            {
                var parts = cached.Split('|');
                if (parts.Length >= 1)
                {
                    DateTime? suspended = null;
                    if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) &&
                        DateTime.TryParse(parts[1], null, System.Globalization.DateTimeStyles.RoundtripKind, out var s))
                        suspended = s;

                    return new SubscriptionAccessSnapshot
                    {
                        Status = parts[0],
                        SuspendedAtUtc = suspended
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscription access cache read failed for {TenantId}", tenantId);
        }

        // Live statuses + suspended (not cancelled). Prefer live unique row, else latest suspended.
        var subscription = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId &&
                        (s.Status == SubscriptionStatuses.Trialing ||
                         s.Status == SubscriptionStatuses.Active ||
                         s.Status == SubscriptionStatuses.PastDue ||
                         s.Status == SubscriptionStatuses.Suspended))
            .OrderByDescending(s => s.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription == null)
            return null;

        var snapshot = new SubscriptionAccessSnapshot
        {
            Status = subscription.Status,
            SuspendedAtUtc = subscription.SuspendedAtUtc
        };

        try
        {
            var payload = $"{snapshot.Status}|{snapshot.SuspendedAtUtc?.ToString("O") ?? ""}";
            await _cache.SetStringAsync(
                BuildKey(tenantId),
                payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscription access cache write failed for {TenantId}", tenantId);
        }

        return snapshot;
    }

    public static async Task InvalidateAsync(IDistributedCache cache, Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            await cache.RemoveAsync(BuildKey(tenantId), ct);
        }
        catch
        {
            // fail-open
        }
    }
}
