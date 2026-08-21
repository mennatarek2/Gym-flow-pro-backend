namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;

public interface IStockCountService
{
    Task<Result<StockCountDto>> CreateAsync(Guid tenantId, Guid identityUserId, CreateStockCountRequest request);
    Task<Result<StockCountDto>> GetAsync(Guid tenantId, Guid id);
    Task<Result<List<StockCountDto>>> ListAsync(Guid tenantId, string? status = null);
    Task<Result<StockCountDto>> UpdateLinesAsync(Guid tenantId, Guid id, UpdateStockCountLinesRequest request);
    Task<Result<StockCountDto>> SubmitAsync(Guid tenantId, Guid identityUserId, Guid id);
    Task<Result<StockCountDto>> ApproveAsync(Guid tenantId, Guid identityUserId, Guid id);
    Task<Result<StockCountDto>> CancelAsync(Guid tenantId, Guid identityUserId, Guid id);
}
