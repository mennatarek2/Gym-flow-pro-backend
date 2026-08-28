namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

public interface ICommercialPlanService
{
    Task<IReadOnlyList<CommercialPlanListItemDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<CommercialPlanDetailDto?> GetAsync(string tier, CancellationToken cancellationToken = default);

    Task<string> GetDefaultTierAsync(CancellationToken cancellationToken = default);

    Task<decimal> GetListPriceForCycleAsync(string tier, string cycle, CancellationToken cancellationToken = default);

    Task<bool> IsActiveForSalesAsync(string tier, CancellationToken cancellationToken = default);

    /// <summary>Returns error message when tier cannot be used for new sales/provisioning.</summary>
    Task<string?> ValidateTierForNewSalesAsync(string tier, CancellationToken cancellationToken = default);

    Task<CommercialPlanMutationResult> UpdateMetadataAsync(
        string tier,
        UpdatePlanMetadataRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<CommercialPlanMutationResult> UpdatePricingAsync(
        string tier,
        UpdatePlanPricingRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<CommercialPlanMutationResult> UpdateCapsAsync(
        string tier,
        UpdatePlanCapsRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<CommercialPlanMutationResult> UpdateFeaturesAsync(
        string tier,
        UpdatePlanFeaturesRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<CommercialPlanMutationResult> SetSalesStatusAsync(
        string tier,
        UpdatePlanSalesStatusRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<CommercialPlanMutationResult> SetDefaultAsync(
        string tier,
        SetDefaultPlanRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<PlatformPagedResult<PlanChangeLogDto>> GetHistoryAsync(
        string tier,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
