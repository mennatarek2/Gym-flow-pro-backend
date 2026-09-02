namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Refunds;

/// <summary>
/// Two-step refund flow (request, then approve-which-executes-immediately, or reject) plus the
/// member account-credit ledger that a 'credit' method refund appends to.
/// </summary>
public interface IRefundService
{
    Task<Result<RefundDto>> RequestAsync(
        Guid saleId, decimal amount, string method, string reason, Guid requestedByUserId, Guid tenantId,
        Guid? paymentTransactionId = null);

    /// <summary>Executes the refund immediately (cash drawer movement, gateway API call, or member
    /// credit) in the same transaction as the approval.</summary>
    Task<Result<RefundDto>> ApproveAsync(Guid refundId, Guid approverUserId, Guid tenantId);

    Task<Result<RefundDto>> RejectAsync(Guid refundId, string note, Guid rejectorUserId, Guid tenantId);

    /// <summary>SUM(Amount) over a member's credit ledger — read-only, race-safe (UPDLOCK+HOLDLOCK'd,
    /// so it's safe to call from within a spending transaction, e.g. SaleService's account_credit
    /// payment leg).</summary>
    Task<decimal> GetMemberCreditBalanceAsync(Guid memberId, Guid tenantId);

    /// <summary>Balance plus the full ledger entry list, for GET /api/members/{id}/credits.</summary>
    Task<Result<MemberCreditSummaryDto>> GetMemberCreditSummaryAsync(Guid memberId, Guid tenantId);

    Task<Result<List<RefundDto>>> GetListAsync(Guid tenantId, Guid? saleId, Guid? memberId, string? status);
}
