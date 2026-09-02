namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Sales;

public interface ISaleAdjustmentService
{
    Task<Result<List<SaleAdjustmentDto>>> ListAsync(
        Guid tenantId,
        Guid? saleId = null,
        CancellationToken ct = default);

    Task<Result<SaleAdjustmentDto>> CreateAsync(
        Guid tenantId,
        Guid identityUserId,
        CreateSaleAdjustmentRequest request,
        CancellationToken ct = default);

    Task<Result<SaleBalanceReconciliationDto>> ReconcileBalanceAsync(
        Guid tenantId,
        Guid identityUserId,
        Guid saleId,
        CancellationToken ct = default);
}
