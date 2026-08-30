namespace GMS.Platform.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using GMS.Core.Interfaces;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class PlatformTenantAdminService : IPlatformTenantAdminService
{
    private readonly PlatformDbContext _db;
    private readonly ISubscriptionWriteRepository _repo;
    private readonly ISubscriptionStatusCache _cache;
    private readonly IDistributedCache _distributedCache;
    private readonly IFeatureAccessService _featureAccess;
    private readonly IPlatformAuditService _audit;

    public PlatformTenantAdminService(
        PlatformDbContext db,
        ISubscriptionWriteRepository repo,
        ISubscriptionStatusCache cache,
        IDistributedCache distributedCache,
        IFeatureAccessService featureAccess,
        IPlatformAuditService audit)
    {
        _db = db;
        _repo = repo;
        _cache = cache;
        _distributedCache = distributedCache;
        _featureAccess = featureAccess;
        _audit = audit;
    }

    public async Task<PlatformActionResult> ApplyCouponAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        CreateCouponRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var discountType = (request.DiscountType ?? string.Empty).Trim().ToLowerInvariant();
        if (discountType is not ("percent" or "fixed"))
            return PlatformActionResult.Fail("INVALID_DISCOUNT_TYPE", "discountType must be percent or fixed.");

        if (request.Value <= 0)
            return PlatformActionResult.Fail("INVALID_VALUE", "value must be greater than zero.");

        if (discountType == "percent" && request.Value > 100)
            return PlatformActionResult.Fail("INVALID_VALUE", "percent discount cannot exceed 100.");

        if (request.ExpiresAtUtc <= DateTime.UtcNow)
            return PlatformActionResult.Fail("INVALID_EXPIRY", "expiresAtUtc must be in the future.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return PlatformActionResult.Fail("REASON_REQUIRED", "reason is required.");

        var row = new PriceOverride
        {
            TenantId = tenantId,
            DiscountType = discountType,
            Value = request.Value,
            ExpiresAtUtc = DateTime.SpecifyKind(request.ExpiresAtUtc, DateTimeKind.Utc),
            Reason = request.Reason.Trim(),
            GrantedByPlatformUserId = actorPlatformUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.PriceOverrides.Add(row);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.tenant.coupon_applied",
            tenantId,
            before: null,
            after: new
            {
                row.Id,
                row.DiscountType,
                row.Value,
                row.ExpiresAtUtc,
                row.Reason
            },
            ipAddress);

        return PlatformActionResult.Ok();
    }

    public async Task<(PlatformActionResult Result, SubscriptionStatusDto? Subscription)> ExtendTrialAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        ExtendTrialRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (request.Days <= 0 || request.Days > 90)
            return (PlatformActionResult.Fail("INVALID_DAYS", "days must be between 1 and 90."), null);

        if (string.IsNullOrWhiteSpace(request.Reason))
            return (PlatformActionResult.Fail("REASON_REQUIRED", "reason is required."), null);

        var subscription = await _repo.GetLiveByTenantAsync(tenantId, cancellationToken);
        if (subscription == null || subscription.Status != SubscriptionStatuses.Trialing)
            return (PlatformActionResult.Fail("NOT_TRIALING", "Tenant has no live trialing subscription."), null);

        var before = Snapshot(subscription);
        var days = request.Days;

        if (subscription.TrialEndsAtUtc.HasValue)
            subscription.TrialEndsAtUtc = subscription.TrialEndsAtUtc.Value.AddDays(days);
        else
            subscription.TrialEndsAtUtc = DateTime.UtcNow.AddDays(days);

        subscription.CurrentPeriodEnd = subscription.CurrentPeriodEnd.AddDays(days);
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repo.SaveWithChangeAsync(subscription, new SubscriptionChange
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            ChangeType = SubscriptionChangeTypes.TrialExtend,
            FromTier = subscription.PlanTier,
            ToTier = subscription.PlanTier,
            EffectiveAtUtc = DateTime.UtcNow,
            InitiatedBy = SubscriptionInitiators.PlatformAdmin,
            PlatformAdminUserId = actorPlatformUserId,
            Reason = request.Reason.Trim()
        }, cancellationToken);

        await _cache.InvalidateAsync(tenantId, cancellationToken);
        await SubscriptionAccessService.InvalidateAsync(_distributedCache, tenantId, cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.tenant.trial_extended",
            tenantId,
            before,
            Snapshot(subscription),
            ipAddress);

        var pending = await _repo.GetPendingDowngradeTierAsync(subscription.Id, cancellationToken);
        return (PlatformActionResult.Ok(), MapStatus(subscription, pending));
    }

    public async Task<(PlatformActionResult Result, SubscriptionStatusDto? Subscription)> ForceSuspendAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        ForceSuspendRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (PlatformActionResult.Fail("REASON_REQUIRED", "reason is required."), null);

        var subscription = await _repo.GetLiveByTenantAsync(tenantId, cancellationToken)
            ?? await _db.Subscriptions
                .Where(s => s.TenantId == tenantId && s.Status != SubscriptionStatuses.Cancelled)
                .OrderByDescending(s => s.UpdatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

        if (subscription == null)
            return (PlatformActionResult.Fail("NO_SUBSCRIPTION", "No subscription found."), null);

        if (subscription.Status == SubscriptionStatuses.Suspended)
            return (PlatformActionResult.Fail("ALREADY_SUSPENDED", "Subscription is already suspended."), null);

        var before = Snapshot(subscription);
        subscription.Status = SubscriptionStatuses.Suspended;
        subscription.SuspendedAtUtc = DateTime.UtcNow;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repo.SaveWithChangeAsync(subscription, new SubscriptionChange
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            ChangeType = SubscriptionChangeTypes.Suspension,
            FromTier = subscription.PlanTier,
            ToTier = subscription.PlanTier,
            EffectiveAtUtc = DateTime.UtcNow,
            InitiatedBy = SubscriptionInitiators.PlatformAdmin,
            PlatformAdminUserId = actorPlatformUserId,
            Reason = request.Reason.Trim()
        }, cancellationToken);

        await _cache.InvalidateAsync(tenantId, cancellationToken);
        await SubscriptionAccessService.InvalidateAsync(_distributedCache, tenantId, cancellationToken);
        await _featureAccess.InvalidateAsync(tenantId, cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.tenant.force_suspend",
            tenantId,
            before,
            Snapshot(subscription),
            ipAddress);

        var pending = await _repo.GetPendingDowngradeTierAsync(subscription.Id, cancellationToken);
        return (PlatformActionResult.Ok(), MapStatus(subscription, pending));
    }

    public async Task<(PlatformActionResult Result, SubscriptionStatusDto? Subscription)> ForceReactivateAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        ForceReactivateRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (PlatformActionResult.Fail("REASON_REQUIRED", "reason is required."), null);

        var subscription = await _db.Subscriptions
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatuses.Suspended)
            .OrderByDescending(s => s.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription == null)
            return (PlatformActionResult.Fail("NOT_SUSPENDED", "No suspended subscription found."), null);

        var before = Snapshot(subscription);
        subscription.Status = SubscriptionStatuses.Active;
        subscription.SuspendedAtUtc = null;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repo.SaveWithChangeAsync(subscription, new SubscriptionChange
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            ChangeType = SubscriptionChangeTypes.Reactivation,
            FromTier = subscription.PlanTier,
            ToTier = subscription.PlanTier,
            EffectiveAtUtc = DateTime.UtcNow,
            InitiatedBy = SubscriptionInitiators.PlatformAdmin,
            PlatformAdminUserId = actorPlatformUserId,
            Reason = request.Reason.Trim()
        }, cancellationToken);

        await _cache.InvalidateAsync(tenantId, cancellationToken);
        await SubscriptionAccessService.InvalidateAsync(_distributedCache, tenantId, cancellationToken);
        await _featureAccess.InvalidateAsync(tenantId, cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.tenant.force_reactivate",
            tenantId,
            before,
            Snapshot(subscription),
            ipAddress);

        var pending = await _repo.GetPendingDowngradeTierAsync(subscription.Id, cancellationToken);
        return (PlatformActionResult.Ok(), MapStatus(subscription, pending));
    }

    public async Task<(PlatformActionResult Result, FeatureOverrideDto? Override)> UpsertFeatureOverrideAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        UpsertFeatureOverrideRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var featureKey = (request.FeatureKey ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(featureKey))
            return (PlatformActionResult.Fail("FEATURE_KEY_REQUIRED", "featureKey is required."), null);

        if (string.IsNullOrWhiteSpace(request.Reason))
            return (PlatformActionResult.Fail("REASON_REQUIRED", "reason is required."), null);

        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc.Value <= DateTime.UtcNow)
            return (PlatformActionResult.Fail("INVALID_EXPIRY", "expiresAtUtc must be in the future when set."), null);

        var existing = await _db.FeatureOverrides
            .Where(o => o.TenantId == tenantId && o.FeatureKey == featureKey)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        object? before = existing == null
            ? null
            : new
            {
                existing.Id,
                existing.FeatureKey,
                existing.Enabled,
                existing.ExpiresAtUtc,
                existing.Reason
            };

        FeatureOverride row;
        if (existing != null &&
            (existing.ExpiresAtUtc == null || existing.ExpiresAtUtc > DateTime.UtcNow))
        {
            existing.Enabled = request.Enabled;
            existing.Reason = request.Reason.Trim();
            existing.GrantedByPlatformUserId = actorPlatformUserId;
            existing.ExpiresAtUtc = request.ExpiresAtUtc.HasValue
                ? DateTime.SpecifyKind(request.ExpiresAtUtc.Value, DateTimeKind.Utc)
                : null;
            row = existing;
        }
        else
        {
            row = new FeatureOverride
            {
                TenantId = tenantId,
                FeatureKey = featureKey,
                Enabled = request.Enabled,
                Reason = request.Reason.Trim(),
                GrantedByPlatformUserId = actorPlatformUserId,
                ExpiresAtUtc = request.ExpiresAtUtc.HasValue
                    ? DateTime.SpecifyKind(request.ExpiresAtUtc.Value, DateTimeKind.Utc)
                    : null,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.FeatureOverrides.Add(row);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _featureAccess.InvalidateAsync(tenantId, cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.tenant.feature_override_upsert",
            tenantId,
            before,
            new
            {
                row.Id,
                row.FeatureKey,
                row.Enabled,
                row.ExpiresAtUtc,
                row.Reason
            },
            ipAddress);

        return (PlatformActionResult.Ok(), MapOverride(row));
    }

    public async Task<PlatformActionResult> DeleteFeatureOverrideAsync(
        Guid tenantId,
        Guid overrideId,
        Guid actorPlatformUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.FeatureOverrides
            .FirstOrDefaultAsync(o => o.Id == overrideId && o.TenantId == tenantId, cancellationToken);

        if (row == null)
            return PlatformActionResult.Fail("NOT_FOUND", "Feature override not found.");

        var before = new
        {
            row.Id,
            row.FeatureKey,
            row.Enabled,
            row.ExpiresAtUtc,
            row.Reason
        };

        _db.FeatureOverrides.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        await _featureAccess.InvalidateAsync(tenantId, cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.tenant.feature_override_deleted",
            tenantId,
            before,
            after: null,
            ipAddress);

        return PlatformActionResult.Ok();
    }

    public async Task<IReadOnlyList<FeatureOverrideDto>> ListFeatureOverridesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _db.FeatureOverrides
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && (o.ExpiresAtUtc == null || o.ExpiresAtUtc > now))
            .OrderBy(o => o.FeatureKey)
            .Select(o => new FeatureOverrideDto
            {
                Id = o.Id,
                TenantId = o.TenantId,
                FeatureKey = o.FeatureKey,
                Enabled = o.Enabled,
                Reason = o.Reason,
                GrantedByPlatformUserId = o.GrantedByPlatformUserId,
                ExpiresAtUtc = o.ExpiresAtUtc,
                CreatedAtUtc = o.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    private static object Snapshot(PlatformSubscription s) => new
    {
        s.Status,
        s.PlanTier,
        s.CurrentPeriodStart,
        s.CurrentPeriodEnd,
        s.TrialEndsAtUtc,
        s.SuspendedAtUtc
    };

    private static SubscriptionStatusDto MapStatus(PlatformSubscription s, string? pending) => new()
    {
        Id = s.Id,
        TenantId = s.TenantId,
        PlanTier = s.PlanTier,
        Status = s.Status,
        BillingCycle = s.BillingCycle,
        PriceEgp = s.PriceEgp,
        CurrentPeriodStart = s.CurrentPeriodStart,
        CurrentPeriodEnd = s.CurrentPeriodEnd,
        TrialEndsAtUtc = s.TrialEndsAtUtc,
        CancelAtPeriodEnd = s.CancelAtPeriodEnd,
        CancelledAtUtc = s.CancelledAtUtc,
        SuspendedAtUtc = s.SuspendedAtUtc,
        UpdatedAtUtc = s.UpdatedAtUtc,
        PendingDowngradeTier = pending,
        HasPaymentMethodOnFile = !string.IsNullOrWhiteSpace(s.SavedCardToken)
    };

    private static FeatureOverrideDto MapOverride(FeatureOverride o) => new()
    {
        Id = o.Id,
        TenantId = o.TenantId,
        FeatureKey = o.FeatureKey,
        Enabled = o.Enabled,
        Reason = o.Reason,
        GrantedByPlatformUserId = o.GrantedByPlatformUserId,
        ExpiresAtUtc = o.ExpiresAtUtc,
        CreatedAtUtc = o.CreatedAtUtc
    };
}
