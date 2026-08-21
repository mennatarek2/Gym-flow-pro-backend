namespace GMS.Core.Interfaces;

/// <summary>
/// Fawry payment gateway integration.
/// </summary>
public interface IFawryService
{
    /// <summary>
    /// Creates a Fawry order and returns the reference number for kiosk/mobile payment.
    /// </summary>
    Task<string> CreateOrderAsync(Guid membershipId, decimal amount);

    /// <summary>
    /// Verifies the SHA-256 signature of an incoming Fawry webhook.
    /// </summary>
    bool VerifyWebhookSignature(byte[] body, string signature);

    /// <summary>
    /// Requests a refund for a previously charged order. Returns false if the gateway doesn't
    /// support/accept the refund (caller should suggest the credit method instead).
    /// </summary>
    Task<bool> RefundAsync(string externalRef, decimal amount);
}
