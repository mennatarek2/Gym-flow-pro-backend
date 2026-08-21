namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Invoices;

/// <summary>
/// Legal invoice/credit-note generation with gap-free sequential numbering per tenant/year/type.
/// </summary>
public interface IInvoiceService
{
    Task<Result<PagedResult<InvoiceDto>>> GetPagedAsync(Guid tenantId, InvoiceQueryRequest query);

    Task<Result<InvoiceDto>> GetByIdAsync(Guid id);

    /// <summary>Re-enqueues the (currently stubbed, P7) delivery job for an already-issued invoice.</summary>
    Task<Result<bool>> ResendAsync(Guid invoiceId);

    /// <summary>
    /// Enqueues the background job that creates the invoice for a completed sale. Skips
    /// enqueueing entirely when the sale's Total is 0 (future trial support).
    /// </summary>
    Task EnqueueForSale(Guid saleId);

    /// <summary>
    /// The background job's actual work: idempotent (backed by the unique index on
    /// (TenantId, InvoiceNumber) plus an explicit pre-check on SaleId+Type) creation of the
    /// Invoice row with a gap-free sequential number. Runs with no ambient tenant context
    /// (Hangfire job scope) — resolves TenantId from the sale itself.
    /// </summary>
    Task CreateForSaleAsync(Guid saleId);

    /// <summary>Issues a credit note reversing the invoice tied to a refund. TODO(P11): the refunds
    /// table doesn't exist yet, so this is a stub until then.</summary>
    Task<Result<Guid>> CreateCreditNoteAsync(Guid refundId);

    /// <summary>Marks an invoice voided. Gated by PaymentsRefundApprove at the controller.</summary>
    Task<Result<bool>> VoidAsync(Guid invoiceId, string reason, Guid voidedByUserId);

    /// <summary>Looks up a specific payment leg, for the receipt-html renderer's "Payment Received"
    /// section (GET /api/invoices/{id}/receipt-html?paymentId=).</summary>
    Task<Result<PaymentReceiptInfoDto>> GetPaymentInfoAsync(Guid paymentTransactionId);

    /// <summary>Finds the original ('invoice'-type) invoice issued for a sale, if any.</summary>
    Task<Result<Guid>> GetOriginalInvoiceIdForSaleAsync(Guid saleId);
}
