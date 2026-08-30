namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

public interface IPlatformTenantReadService
{
    /// <summary>
    /// <paramref name="renewingBefore"/>: server-side date filter — only tenants whose live subscription's
    /// CurrentPeriodEnd falls on or before this date (inclusive). Never filter "expiring soon" client-side.
    /// </summary>
    Task<PlatformPagedResult<PlatformTenantListItemDto>> ListAsync(
        string? status,
        string? tier,
        string? riskBand,
        string? search,
        int page,
        int pageSize,
        DateOnly? renewingBefore = null,
        bool? hasSubscription = null,
        CancellationToken cancellationToken = default);

    Task<PlatformTenantDetailDto?> GetDetailAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionChangeDto>> GetSubscriptionChangesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformInvoiceDto>> GetInvoicesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
