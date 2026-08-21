namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

public interface IPlatformTenantReadService
{
    Task<PlatformPagedResult<PlatformTenantListItemDto>> ListAsync(
        string? status,
        string? tier,
        string? riskBand,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PlatformTenantDetailDto?> GetDetailAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionChangeDto>> GetSubscriptionChangesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformInvoiceDto>> GetInvoicesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
