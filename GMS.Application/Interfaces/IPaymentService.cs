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
        Guid memberId, Guid tenantId, string? rawPayload, bool hmacVerified);
}
