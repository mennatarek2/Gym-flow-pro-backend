namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;

/// <summary>Single SoT for reorder suggestions (reports + from-suggestions).</summary>
public interface IInventoryReorderCalculator
{
    /// <param name="productIds">When null/empty, all eligible purchasable products.</param>
    /// <param name="includeCost">When false, CostPrice is null on rows.</param>
    Task<Result<List<ReorderCalcRow>>> CalculateAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid>? productIds = null,
        bool includeCost = false,
        CancellationToken ct = default);
}
