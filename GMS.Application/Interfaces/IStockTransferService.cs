namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;

public interface IStockTransferService
{
    Task<Result<StockTransferDto>> CreatePendingAsync(Guid tenantId, Guid identityUserId, CreateStockTransferRequest request);
    Task<Result<StockTransferDto>> GetAsync(Guid tenantId, Guid id);
    Task<Result<InventoryListPageDto<StockTransferDto>>> ListAsync(Guid tenantId, string? status = null);
    Task<Result<StockTransferDto>> SubmitAsync(Guid tenantId, Guid identityUserId, Guid id);
    Task<Result<StockTransferDto>> ReceiveAsync(Guid tenantId, Guid identityUserId, Guid id);
    Task<Result<StockTransferDto>> CancelAsync(Guid tenantId, Guid identityUserId, Guid id);
    /// <summary>Reject in-transit transfer: return qty to source warehouse, status cancelled.</summary>
    Task<Result<StockTransferDto>> RejectAsync(Guid tenantId, Guid identityUserId, Guid id);
}
