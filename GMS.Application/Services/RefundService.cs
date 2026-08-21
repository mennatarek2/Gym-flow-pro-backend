namespace GMS.Application.Services;

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Application.DTOs.Refunds;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Two-step refund flow: RequestAsync validates against the sale's refundable remainder and creates
/// a 'requested' row; ApproveAsync executes the refund immediately (cash drawer movement, gateway
/// API call, or member credit ledger entry) in the same transaction as the approval, then updates the
/// sale's status and cancels the membership on a full refund. RejectAsync just records the rejection.
/// </summary>
public class RefundService : IRefundService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IShiftService _shiftService;
    private readonly IInvoiceService _invoiceService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IPaymobService _paymobService;
    private readonly IFawryService _fawryService;
    private readonly IAuditService _auditService;
    private readonly IReferralRewardService _referralRewards;
    private readonly IStockLedgerService _stockLedger;
    private readonly ILogger<RefundService> _logger;

    public RefundService(
        GymFlowProDbContext dbContext,
        IShiftService shiftService,
        IInvoiceService invoiceService,
        IWhatsAppService whatsAppService,
        IPaymobService paymobService,
        IFawryService fawryService,
        IAuditService auditService,
        IReferralRewardService referralRewards,
        IStockLedgerService stockLedger,
        ILogger<RefundService> logger)
    {
        _dbContext = dbContext;
        _shiftService = shiftService;
        _invoiceService = invoiceService;
        _whatsAppService = whatsAppService;
        _paymobService = paymobService;
        _fawryService = fawryService;
        _auditService = auditService;
        _referralRewards = referralRewards;
        _stockLedger = stockLedger;
        _logger = logger;
    }

    public async Task<Result<RefundDto>> RequestAsync(
        Guid saleId, decimal amount, string method, string reason, Guid requestedByUserId, Guid tenantId)
    {
        try
        {
            var sale = await _dbContext.Sales.FirstOrDefaultAsync(s => s.Id == saleId && s.TenantId == tenantId);
            if (sale == null)
                return Fail(RefundFailureReasons.SaleNotFound, "Sale not found / عملية البيع غير موجودة");

            var requester = await ResolveStaffUserAsync(requestedByUserId, tenantId);
            if (requester == null)
                return Fail(RefundFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            var alreadyExecuted = await _dbContext.Refunds
                .Where(r => r.SaleId == saleId && r.TenantId == tenantId && r.Status == "executed")
                .SumAsync(r => (decimal?)r.Amount) ?? 0m;

            var remainder = sale.Total - alreadyExecuted;

            if (remainder <= 0m)
                return Fail(RefundFailureReasons.SaleFullyRefunded, "This sale has already been fully refunded / تم استرداد قيمة عملية البيع بالكامل");

            if (amount > remainder)
                return Fail(RefundFailureReasons.RefundExceedsRemainder,
                    "Refund amount exceeds the refundable remainder for this sale / مبلغ الاسترداد يتجاوز المبلغ القابل للاسترداد لهذه العملية");

            // Best-effort auto-resolution of the specific payment being reversed, so ApproveAsync
            // knows which gateway (paymob/fawry) to call for a 'gateway' method refund.
            var paymentTransactionId = await ResolvePaymentTransactionIdAsync(saleId, tenantId, method);

            var refund = new Refund
            {
                TenantId = tenantId,
                SaleId = saleId,
                PaymentTransactionId = paymentTransactionId,
                Amount = amount,
                Method = method,
                Reason = reason,
                RequestedByUserId = requester.Id,
                Status = "requested"
            };

            _dbContext.Refunds.Add(refund);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Refund requested: {RefundId} for sale {SaleId}, amount {Amount} ({Method})",
                refund.Id, saleId, amount, method);

            return Result<RefundDto>.Success(ToDto(refund));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting refund for sale {SaleId}", saleId);
            return Result<RefundDto>.Failure("Failed to request refund / فشل طلب الاسترداد", ex.Message);
        }
    }

    public async Task<Result<RefundDto>> ApproveAsync(Guid refundId, Guid approverUserId, Guid tenantId)
    {
        var isRelational = _dbContext.Database.IsRelational();
        var transaction = isRelational
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
            : null;

        try
        {
            var refund = await _dbContext.Refunds
                .Include(r => r.PaymentTransaction)
                .FirstOrDefaultAsync(r => r.Id == refundId && r.TenantId == tenantId);

            if (refund == null)
                return Fail(RefundFailureReasons.RefundNotFound, "Refund not found / طلب الاسترداد غير موجود");

            if (refund.Status != "requested")
                return Fail(RefundFailureReasons.NotAwaitingApproval,
                    "This refund is not awaiting approval / طلب الاسترداد هذا ليس بانتظار الموافقة");

            var approver = await ResolveStaffUserAsync(approverUserId, tenantId);
            if (approver == null)
                return Fail(RefundFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            var isOwner = string.Equals(approver.Role, "Owner", StringComparison.OrdinalIgnoreCase);
            if (!isOwner && approver.Id == refund.RequestedByUserId)
                return Fail(RefundFailureReasons.SelfApprovalForbidden,
                    "You cannot approve a refund you requested yourself / لا يمكنك اعتماد طلب استرداد قدّمته بنفسك");

            var sale = await _dbContext.Sales
                .Include(s => s.Member)
                .FirstOrDefaultAsync(s => s.Id == refund.SaleId && s.TenantId == tenantId);

            if (sale == null)
                return Fail(RefundFailureReasons.SaleNotFound, "Sale not found / عملية البيع غير موجودة");

            await _auditService.LogAsync("refund.approved", "Refund", refund.Id, null,
                new { approvedByUserId = approver.Id, method = refund.Method, amount = refund.Amount });

            switch (refund.Method)
            {
                case "cash":
                {
                    var shiftId = await _shiftService.GetCurrentOpenShiftIdAsync(approverUserId, tenantId);
                    if (shiftId == null)
                        return Fail(RefundFailureReasons.OpenShiftRequired,
                            "An open shift is required to refund cash / يجب فتح وردية لاسترداد النقد");

                    var movementResult = await _shiftService.RecordMovementAsync(
                        shiftId.Value, "refund", refund.Amount, refund.Id, refund.Reason, approverUserId, tenantId);

                    if (!movementResult.IsSuccess)
                        return Fail("CASH_MOVEMENT_FAILED", movementResult.Error ?? "Failed to record the cash movement / فشل تسجيل الحركة النقدية");

                    break;
                }

                case "gateway":
                {
                    var gatewayMethod = refund.PaymentTransaction?.Method;
                    var externalRef = refund.PaymentTransaction?.ExternalRef;

                    bool refunded;
                    if (gatewayMethod == "card_paymob" && !string.IsNullOrEmpty(externalRef))
                        refunded = await _paymobService.RefundAsync(externalRef, refund.Amount);
                    else if (gatewayMethod == "fawry" && !string.IsNullOrEmpty(externalRef))
                        refunded = await _fawryService.RefundAsync(externalRef, refund.Amount);
                    else
                        refunded = false;

                    if (!refunded)
                        return Fail(RefundFailureReasons.GatewayRefundUnsupported,
                            "This payment's gateway does not support refunds — use the credit method instead / لا تدعم بوابة الدفع هذه عمليات الاسترداد، استخدم طريقة الرصيد بدلاً من ذلك");

                    break;
                }

                case "credit":
                {
                    var memberId = sale.MemberId ?? Guid.Empty;
                    _dbContext.MemberCredits.Add(new MemberCredit
                    {
                        TenantId = tenantId,
                        MemberId = memberId,
                        Amount = refund.Amount,
                        EntryType = "refund",
                        ReferenceId = refund.Id,
                        Reason = refund.Reason,
                        CreatedByUserId = approver.Id
                    });
                    break;
                }
            }

            refund.Status = "executed";
            refund.ApprovedByUserId = approver.Id;
            refund.ExecutedAt = DateTime.UtcNow;
            refund.UpdatedAtUtc = DateTime.UtcNow;

            var executedTotal = await _dbContext.Refunds
                .Where(r => r.SaleId == sale.Id && r.TenantId == tenantId && r.Status == "executed")
                .SumAsync(r => (decimal?)r.Amount) ?? 0m;
            executedTotal += refund.Amount;

            sale.Status = executedTotal >= sale.Total ? "refunded" : "partially_refunded";
            sale.UpdatedAtUtc = DateTime.UtcNow;

            if (sale.Status == "refunded")
            {
                var membershipId = await _dbContext.SaleLines
                    .Where(l => l.SaleId == sale.Id && l.TenantId == tenantId && l.LineType == "membership")
                    .Select(l => l.ReferenceId)
                    .FirstOrDefaultAsync();

                if (membershipId.HasValue)
                {
                    var membership = await _dbContext.Memberships
                        .FirstOrDefaultAsync(m => m.Id == membershipId.Value && m.TenantId == tenantId);

                    if (membership != null)
                    {
                        membership.Status = "cancelled";
                        membership.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }
            }

            // INVS-7: restore retail stock only on full sale refund (including credit method).
            // Partial amount refunds leave stock unchanged until line-level refunds exist.
            var stockRestored = false;
            if (sale.Status == "refunded")
            {
                var restore = await RestoreRetailStockAsync(sale.Id, refund.Id, tenantId, approver.Id);
                if (!restore.IsSuccess)
                {
                    var err = restore.Error ?? "Stock restore failed / فشل إرجاع المخزون";
                    var pipe = err.IndexOf('|');
                    return pipe > 0
                        ? Fail(err[..pipe], err[(pipe + 1)..])
                        : Fail(RefundFailureReasons.StockRestoreFailed, err);
                }

                stockRestored = restore.Data;
            }

            await _dbContext.SaveChangesAsync();
            if (transaction != null)
            {
                await transaction.CommitAsync();
                // Dispose (not just commit) before any post-commit call opens its own transaction on
                // this same shared DbContext (InvoiceService.CreateCreditNoteAsync does) — leaving a
                // committed-but-undisposed transaction handle around risks it conflicting with a new one.
                await transaction.DisposeAsync();
                transaction = null;
            }

            await _auditService.LogAsync("refund.executed", "Refund", refund.Id, null,
                new { executedAt = refund.ExecutedAt, saleStatus = sale.Status, stockRestored });

            _logger.LogInformation("Refund executed: {RefundId} for sale {SaleId}, amount {Amount} ({Method}), stockRestored={StockRestored}",
                refund.Id, sale.Id, refund.Amount, refund.Method, stockRestored);

            if (sale.Status == "refunded")
            {
                try
                {
                    await _referralRewards.HandleConvertingSaleRefundedAsync(tenantId, sale.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Referral reward forfeit/reverse failed for refunded sale {SaleId}", sale.Id);
                }
            }

            // Post-commit side effects — logged, not propagated, so a failure here doesn't make the
            // caller think the already-executed refund failed (mirrors SaleService's post-commit steps).
            // A 'credit' refund doesn't reverse revenue — the business keeps the money and issues
            // store credit (a liability, tracked in member_credits) instead — so no legal credit note
            // is issued for it; only cash/gateway refunds (an actual reversal of money received)
            // get one, matching invoices.Total(invoice) - invoices.Total(credit_note) reconciling
            // exactly against payment_transactions minus non-credit refunds.
            if (refund.Method != "credit")
            {
                var creditNoteResult = await _invoiceService.CreateCreditNoteAsync(refund.Id);
                if (!creditNoteResult.IsSuccess)
                    _logger.LogWarning("Failed to create credit note for refund {RefundId}: {Error}", refund.Id, creditNoteResult.Error);
            }

            if (sale.Member != null)
            {
                _ = _whatsAppService.SendTemplateAsync(sale.Member.PhoneNumber, "refund_confirmed", new Dictionary<string, string>
                {
                    ["memberName"] = sale.Member.FullName,
                    ["amount"] = refund.Amount.ToString("F2"),
                    ["method"] = refund.Method
                });
            }

            return Result<RefundDto>.Success(ToDto(refund, stockRestored));
        }
        catch (Exception ex)
        {
            if (transaction != null)
                await transaction.RollbackAsync();

            _logger.LogError(ex, "Error approving refund {RefundId}", refundId);
            return Result<RefundDto>.Failure("Failed to approve refund / فشل اعتماد الاسترداد", ex.Message);
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    /// <summary>
    /// Restores full retail qty for a fully refunded sale using the original sale warehouse/batch
    /// from each <c>sale</c> movement. Idempotent via ledger (RefundSaleLine + SaleLine.Id).
    /// </summary>
    private async Task<Result<bool>> RestoreRetailStockAsync(
        Guid saleId, Guid refundId, Guid tenantId, Guid approverAppUserId)
    {
        var retailLines = await _dbContext.SaleLines
            .Where(l => l.SaleId == saleId && l.TenantId == tenantId
                && l.LineType == "retail")
            .ToListAsync();

        if (retailLines.Count == 0)
            return Result<bool>.Success(false);

        var anyRestored = false;
        foreach (var line in retailLines)
        {
            var saleMovements = await _dbContext.StockMovements
                .AsNoTracking()
                .Where(m =>
                    m.TenantId == tenantId
                    && m.ReferenceType == StockReferenceTypes.SaleLine
                    && m.ReferenceId == line.Id
                    && m.Reason == StockMovementReasons.Sale)
                .ToListAsync();

            if (saleMovements.Count == 0)
            {
                // Non-stocked retail (TrackStock=false) never posted a sale movement — skip.
                if (line.ReferenceId.HasValue)
                {
                    var product = await _dbContext.Products.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == line.ReferenceId.Value && p.TenantId == tenantId);
                    if (product != null && !product.TrackStock)
                        continue;
                }

                return Result<bool>.Failure(
                    $"{RefundFailureReasons.OriginalSaleMovementMissing}|Missing original sale stock movement for line {line.Id} / حركة المخزون الأصلية للسطر غير موجودة");
            }

            foreach (var saleMovement in saleMovements)
            {
                var qty = Math.Abs(saleMovement.QtyDelta);
                if (qty <= 0)
                    continue;

                var post = await _stockLedger.PostAsync(new StockLedgerPostRequest
                {
                    TenantId = tenantId,
                    ProductId = saleMovement.ProductId,
                    WarehouseId = saleMovement.WarehouseId,
                    BatchId = saleMovement.BatchId,
                    QtyDelta = qty,
                    UnitCost = saleMovement.UnitCost,
                    Reason = StockMovementReasons.SaleRefund,
                    ReferenceType = StockReferenceTypes.RefundSaleLine,
                    ReferenceId = line.Id,
                    Note = $"Refund {refundId:N}",
                    CreatedByUserId = approverAppUserId
                });

                if (!post.IsSuccess)
                    return Result<bool>.Failure(
                        $"{RefundFailureReasons.StockRestoreFailed}|{post.Error}");

                anyRestored = true;
            }
        }

        return Result<bool>.Success(anyRestored);
    }

    public async Task<Result<RefundDto>> RejectAsync(Guid refundId, string note, Guid rejectorUserId, Guid tenantId)
    {
        try
        {
            var refund = await _dbContext.Refunds.FirstOrDefaultAsync(r => r.Id == refundId && r.TenantId == tenantId);
            if (refund == null)
                return Fail(RefundFailureReasons.RefundNotFound, "Refund not found / طلب الاسترداد غير موجود");

            if (refund.Status != "requested")
                return Fail(RefundFailureReasons.NotAwaitingApproval,
                    "This refund is not awaiting approval / طلب الاسترداد هذا ليس بانتظار الموافقة");

            var rejector = await ResolveStaffUserAsync(rejectorUserId, tenantId);
            if (rejector == null)
                return Fail(RefundFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            refund.Status = "rejected";
            refund.RejectionNote = note;
            refund.ApprovedByUserId = rejector.Id; // records who acted on the request, same column used by ApproveAsync
            refund.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("refund.rejected", "Refund", refund.Id, null, new { rejectedByUserId = rejector.Id, note });

            return Result<RefundDto>.Success(ToDto(refund));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting refund {RefundId}", refundId);
            return Result<RefundDto>.Failure("Failed to reject refund / فشل رفض الاسترداد", ex.Message);
        }
    }

    public async Task<decimal> GetMemberCreditBalanceAsync(Guid memberId, Guid tenantId)
    {
        var results = await _dbContext.Database
            .SqlQuery<decimal>(
                $"SELECT ISNULL(SUM(Amount), 0) FROM member_credits WITH (UPDLOCK, HOLDLOCK) WHERE MemberId = {memberId} AND TenantId = {tenantId} AND IsDeleted = 0")
            .ToListAsync();

        return results.Count > 0 ? results[0] : 0m;
    }

    public async Task<Result<MemberCreditSummaryDto>> GetMemberCreditSummaryAsync(Guid memberId, Guid tenantId)
    {
        try
        {
            var balance = await GetMemberCreditBalanceAsync(memberId, tenantId);

            var entries = await _dbContext.MemberCredits
                .Where(c => c.MemberId == memberId && c.TenantId == tenantId)
                .OrderByDescending(c => c.CreatedAtUtc)
                .Select(c => new MemberCreditEntryDto
                {
                    Id = c.Id,
                    Amount = c.Amount,
                    EntryType = c.EntryType,
                    ReferenceId = c.ReferenceId,
                    Reason = c.Reason,
                    CreatedAtUtc = c.CreatedAtUtc
                })
                .ToListAsync();

            return Result<MemberCreditSummaryDto>.Success(new MemberCreditSummaryDto { Balance = balance, Entries = entries });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving credit summary for member {MemberId}", memberId);
            return Result<MemberCreditSummaryDto>.Failure("Failed to retrieve member credit summary / فشل جلب رصيد العضو", ex.Message);
        }
    }

    public async Task<Result<List<RefundDto>>> GetListAsync(Guid tenantId, Guid? saleId, Guid? memberId, string? status)
    {
        try
        {
            var query = _dbContext.Refunds.Where(r => r.TenantId == tenantId);

            if (saleId.HasValue)
                query = query.Where(r => r.SaleId == saleId.Value);

            if (memberId.HasValue)
                query = query.Where(r => r.Sale != null && r.Sale.MemberId == memberId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(r => r.Status == status);

            var refunds = await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync();

            return Result<List<RefundDto>>.Success(refunds.Select(r => ToDto(r)).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing refunds for tenant {TenantId}", tenantId);
            return Result<List<RefundDto>>.Failure("Failed to retrieve refunds / فشل جلب طلبات الاسترداد", ex.Message);
        }
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    private async Task<AppUser?> ResolveStaffUserAsync(Guid identityUserId, Guid tenantId)
    {
        var identityUserIdStr = identityUserId.ToString();
        return await _dbContext.AppUsers
            .FirstOrDefaultAsync(u => u.UserId == identityUserIdStr && u.TenantId == tenantId);
    }

    /// <summary>Best-effort match of the specific PaymentTransaction leg this refund reverses, based
    /// on the refund's broad category ('cash'/'gateway'/'credit') vs. PaymentTransaction.Method's
    /// specific value ('cash'/'card_paymob'/'fawry'/'vodafone'/'instapay'/'account_credit').</summary>
    private async Task<Guid?> ResolvePaymentTransactionIdAsync(Guid saleId, Guid tenantId, string method)
    {
        var candidateMethods = method switch
        {
            "cash" => new[] { "cash" },
            "credit" => new[] { "account_credit" },
            "gateway" => new[] { "card_paymob", "fawry", "vodafone", "instapay" },
            _ => Array.Empty<string>()
        };

        if (candidateMethods.Length == 0)
            return null;

        return await _dbContext.PaymentTransactions
            .Where(p => p.SaleId == saleId && p.TenantId == tenantId && p.Method != null && candidateMethods.Contains(p.Method))
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync();
    }

    private static Result<RefundDto> Fail(string code, string message) =>
        Result<RefundDto>.Failure($"{code}|{message}");

    private static RefundDto ToDto(Refund r, bool stockRestored = false) => new()
    {
        Id = r.Id,
        SaleId = r.SaleId,
        PaymentTransactionId = r.PaymentTransactionId,
        Amount = r.Amount,
        Method = r.Method,
        Reason = r.Reason,
        RequestedByUserId = r.RequestedByUserId,
        ApprovedByUserId = r.ApprovedByUserId,
        Status = r.Status,
        RejectionNote = r.RejectionNote,
        CreditNoteInvoiceId = r.CreditNoteInvoiceId,
        ExecutedAt = r.ExecutedAt,
        CreatedAtUtc = r.CreatedAtUtc,
        StockRestored = stockRestored
    };
}
