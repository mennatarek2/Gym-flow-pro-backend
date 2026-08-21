namespace GMS.Core.Interfaces;

/// <summary>
/// Paymob payment gateway integration.
/// </summary>
public interface IPaymobService
{
    /// <summary>
    /// Creates a payment intent and returns the redirect URL for the member.
    /// </summary>
    Task<string> CreatePaymentIntentAsync(Guid membershipId, decimal amount, string memberPhone);

    /// <summary>
    /// Verifies the HMAC-SHA512 signature of an incoming webhook.
    /// </summary>
    bool VerifyWebhookSignature(byte[] body, string hmacHeader);

    /// <summary>
    /// Requests a refund for a previously captured transaction. Returns false if the gateway
    /// doesn't support/accept the refund (caller should suggest the credit method instead).
    /// </summary>
    Task<bool> RefundAsync(string externalRef, decimal amount);
}
