namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.MemberStore;

public interface IMemberStoreService
{
    Task<Result<List<MemberStoreProductDto>>> ListStoreProductsAsync(Guid tenantId, string? q = null, CancellationToken ct = default);

    Task<Result<MemberOrderDto>> CreateOrderAsync(
        Guid tenantId, Guid identityUserId, CreateMemberOrderRequest request, CancellationToken ct = default);

    Task<Result<List<MemberOrderListItemDto>>> ListMyOrdersAsync(
        Guid tenantId, Guid identityUserId, CancellationToken ct = default);

    Task<Result<MemberOrderDto>> GetMyOrderAsync(
        Guid tenantId, Guid identityUserId, Guid orderId, CancellationToken ct = default);

    Task<Result<List<MemberOrderListItemDto>>> ListOrdersForStaffAsync(
        Guid tenantId, string? status = null, Guid? memberId = null, CancellationToken ct = default);

    Task<Result<MemberOrderDto>> GetOrderForStaffAsync(
        Guid tenantId, Guid orderId, CancellationToken ct = default);

    Task<Result<MemberOrderDto>> AcceptAsync(
        Guid tenantId, Guid orderId, Guid identityUserId, CancellationToken ct = default);

    Task<Result<MemberOrderDto>> RejectAsync(
        Guid tenantId, Guid orderId, Guid identityUserId, RejectMemberOrderRequest request, CancellationToken ct = default);

    Task<Result<MemberOrderDto>> MarkReadyAsync(
        Guid tenantId, Guid orderId, Guid identityUserId, CancellationToken ct = default);

    /// <summary>
    /// Ready → Completed: creates a paid retail cash sale (stock + invoice) via <see cref="ISaleService"/>,
    /// then links <c>SaleId</c>. Requires an open shift for cash. Idempotent via sale idempotency key.
    /// </summary>
    Task<Result<MemberOrderDto>> CompleteAsync(
        Guid tenantId,
        Guid orderId,
        Guid identityUserId,
        IReadOnlySet<string>? callerPermissions = null,
        CancellationToken ct = default);
}
