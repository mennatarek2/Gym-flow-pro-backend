namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;

public interface IStockAdjustmentService
{
    Task<Result<StockAdjustmentDto>> CreateDraftAsync(
        Guid tenantId, Guid identityUserId, CreateStockAdjustmentRequest request);

    Task<Result<StockAdjustmentDto>> GetAsync(Guid tenantId, Guid id);

    Task<Result<List<StockAdjustmentDto>>> ListAsync(
        Guid tenantId, string? status = null, int take = 50);

    Task<Result<StockAdjustmentDto>> PostAsync(
        Guid tenantId, Guid identityUserId, Guid adjustmentId);

    Task<Result<StockAdjustmentDto>> CancelAsync(
        Guid tenantId, Guid identityUserId, Guid adjustmentId);
}
