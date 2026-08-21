namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;

/// <summary>
/// Sole writer of <c>stock_movements</c> / <c>stock_balances</c> (INVS-3).
/// </summary>
public interface IStockLedgerService
{
    Task<Result<StockMovementDto>> PostAsync(StockLedgerPostRequest request, CancellationToken ct = default);

    /// <summary>
    /// Physical on-hand. When <paramref name="batchId"/> is null, sums all batch buckets
    /// (including expired). When set, returns that bucket only.
    /// </summary>
    Task<Result<decimal>> GetOnHandAsync(
        Guid tenantId, Guid productId, Guid warehouseId, Guid? batchId = null, CancellationToken ct = default);

    /// <summary>Sellable qty at warehouse (excludes expired batches for TrackExpiry products).</summary>
    Task<Result<decimal>> GetAvailableAsync(
        Guid tenantId, Guid productId, Guid warehouseId, CancellationToken ct = default);

    /// <summary>
    /// Silent FEFO (expiry ASC, then batch created ASC). Null-batch last.
    /// Does not post — caller posts each slice.
    /// </summary>
    Task<Result<List<StockAllocationSlice>>> AllocateSaleAsync(
        Guid tenantId, Guid productId, Guid warehouseId, decimal qty, CancellationToken ct = default);

    Task<Result<StockQueryResponse>> QueryStockAsync(
        Guid tenantId, Guid productId, Guid warehouseId, bool includeMovements = false, int movementTake = 50,
        CancellationToken ct = default);

    Task<Result<ProductStockBreakdownDto>> GetProductStockBreakdownAsync(
        Guid tenantId, Guid productId, CancellationToken ct = default);

    Task<Result<List<StockBoardRowDto>>> GetStockBoardAsync(
        Guid tenantId, Guid? warehouseId = null, string? q = null, CancellationToken ct = default);
}
