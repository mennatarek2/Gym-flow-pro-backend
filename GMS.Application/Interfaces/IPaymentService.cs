namespace GMS.Application.Interfaces;

using GMS.Application.Common;

/// <summary>
/// Payment processing service.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Handles a successful payment webhook.
    /// Creates payment transaction, renews membership, enqueues WhatsApp confirmation.
    /// Idempotent — duplicate webhooks with same externalRef are ignored.
    /// </summary>
    Task<Result<string>> HandleSuccessfulPaymentAsync(
        string gateway, string externalRef, decimal amount,
        Guid memberId, Guid tenantId, string? rawPayload, bool hmacVerified,
        Guid? saleId = null, string? paymentMethod = null);

    Task<Result<string>> RecordFailedPaymentAsync(
        string gateway, string externalRef, decimal amount,
        Guid memberId, Guid tenantId, string? rawPayload, bool hmacVerified,
        Guid? saleId = null, string? paymentMethod = null);

    /// <summary>
    /// Marks a successful electronic payment settled only when a separately verified
    /// provider settlement event has been received.
    /// </summary>
    Task<Result<string>> ConfirmSettlementAsync(
        Guid paymentTransactionId,
        Guid tenantId,
        string gateway,
        string externalRef,
        string? rawPayload,
        bool externalEvidenceVerified);
}
