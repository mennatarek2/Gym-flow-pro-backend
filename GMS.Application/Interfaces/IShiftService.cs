namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Shifts;

/// <summary>
/// Cash-drawer shift management: open/close with reconciliation, cash movement recording, and
/// variance approval. All *UserId parameters are the JWT "sub" (ApplicationUser.Id) — resolved
/// internally to app_users.Id, same distinction CheckinService/SaleService rely on.
/// </summary>
public interface IShiftService
{
    Task<Result<ShiftDto>> OpenAsync(decimal openingFloat, Guid staffUserId, Guid tenantId);

    /// <summary>Returns Success with null Data if the caller has no open shift.</summary>
    Task<Result<ShiftDto>> GetCurrentAsync(Guid staffUserId, Guid tenantId);

    /// <summary>Lightweight lookup used by SaleService — returns null if no shift is open.</summary>
    Task<Guid?> GetCurrentOpenShiftIdAsync(Guid staffUserId, Guid tenantId);

    Task<Result<CashMovementDto>> RecordMovementAsync(
        Guid shiftId, string type, decimal amount, Guid? referenceId, string? reason,
        Guid staffUserId, Guid tenantId, IReadOnlySet<string>? callerPermissions = null);

    Task<Result<ShiftDto>> CloseAsync(decimal countedCash, string? varianceNote, Guid staffUserId, Guid tenantId);

    Task<Result<ShiftDto>> ApproveAsync(Guid shiftId, string? note, Guid approverUserId, Guid tenantId);

    Task<Result<ShiftDto>> ForceCloseAsync(Guid shiftId, Guid managerUserId, Guid tenantId);

    Task<Result<PagedResult<ShiftDto>>> GetPagedAsync(
        Guid tenantId, DateTime? from, DateTime? to, Guid? userId, int page, int pageSize);

    Task<Result<ShiftOpenSummaryDto>> GetOpenSummaryAsync(Guid tenantId);
}
