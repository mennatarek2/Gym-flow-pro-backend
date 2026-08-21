namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;

public interface ISupplierService
{
    Task<Result<List<SupplierDto>>> ListAsync(Guid tenantId, bool includeInactive = false, bool includeMoney = false);
    Task<Result<SupplierDto>> GetAsync(Guid tenantId, Guid id, bool includeMoney = false);
    Task<Result<SupplierDto>> CreateAsync(Guid tenantId, CreateSupplierRequest request);
    Task<Result<SupplierDto>> UpdateAsync(Guid tenantId, Guid id, UpdateSupplierRequest request);
    Task<Result<SupplierBalanceDto>> GetBalanceAsync(Guid tenantId, Guid supplierId);
    Task<Result<InventoryListPageDto<SupplierLedgerEntryDto>>> ListLedgerAsync(
        Guid tenantId, Guid supplierId, DateTime? fromUtc = null, DateTime? toUtc = null);
    Task<Result<SupplierLedgerEntryDto>> PostOpeningAsync(
        Guid tenantId, Guid supplierId, PostSupplierOpeningRequest request);
    Task<Result<SupplierLedgerEntryDto>> PostPaymentAsync(
        Guid tenantId, Guid supplierId, PostSupplierPaymentRequest request);
}

public interface IPurchaseOrderService
{
    Task<Result<PurchaseOrderDto>> CreateDraftAsync(Guid tenantId, CreatePurchaseOrderRequest request);
    Task<Result<PurchaseOrderDto>> CreateDraftFromSuggestionsAsync(
        Guid tenantId, CreatePoFromSuggestionsRequest request);
    Task<Result<PurchaseOrderDto>> GetAsync(Guid tenantId, Guid id);
    Task<Result<InventoryListPageDto<PurchaseOrderDto>>> ListAsync(Guid tenantId, string? status = null);
    Task<Result<PurchaseOrderDto>> ApproveAsync(Guid tenantId, Guid identityUserId, Guid id);
    Task<Result<PurchaseOrderDto>> CancelAsync(Guid tenantId, Guid id);
    Task<Result<GoodsReceiptDto>> ReceiveAsync(
        Guid tenantId, Guid identityUserId, Guid purchaseOrderId, ReceivePurchaseOrderRequest request);
    Task<Result<InventoryListPageDto<GoodsReceiptListItemDto>>> ListGoodsReceiptsAsync(
        Guid tenantId, DateTime? fromUtc = null, DateTime? toUtc = null, Guid? supplierId = null);
    Task<Result<GoodsReceiptDto>> GetGoodsReceiptAsync(Guid tenantId, Guid id);
}
