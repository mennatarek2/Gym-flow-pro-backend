namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

public interface IPlatformTenantAdminService
{
    Task<PlatformActionResult> ApplyCouponAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        CreateCouponRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<(PlatformActionResult Result, SubscriptionStatusDto? Subscription)> ExtendTrialAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        ExtendTrialRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<(PlatformActionResult Result, SubscriptionStatusDto? Subscription)> ForceSuspendAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        ForceSuspendRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<(PlatformActionResult Result, SubscriptionStatusDto? Subscription)> ForceReactivateAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        ForceReactivateRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<(PlatformActionResult Result, FeatureOverrideDto? Override)> UpsertFeatureOverrideAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        UpsertFeatureOverrideRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<PlatformActionResult> DeleteFeatureOverrideAsync(
        Guid tenantId,
        Guid overrideId,
        Guid actorPlatformUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureOverrideDto>> ListFeatureOverridesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
