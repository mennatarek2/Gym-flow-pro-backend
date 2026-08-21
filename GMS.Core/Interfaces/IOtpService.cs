namespace GMS.Core.Interfaces;

/// <summary>
/// Service for OTP generation, storage, and validation.
/// Used for member mobile app authentication via phone number.
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Generates a 6-digit OTP for the given phone number and tenant.
    /// Stores the OTP in cache with a 5-minute expiry.
    /// </summary>
    /// <param name="phoneNumber">Member's phone number.</param>
    /// <param name="tenantId">Tenant ID for multi-tenant isolation.</param>
    /// <returns>The generated 6-digit OTP string.</returns>
    Task<string> GenerateOtpAsync(string phoneNumber, Guid tenantId);

    /// <summary>
    /// Validates the OTP provided by the member.
    /// Removes the OTP from cache on successful validation (single-use).
    /// </summary>
    /// <param name="phoneNumber">Member's phone number.</param>
    /// <param name="tenantId">Tenant ID for multi-tenant isolation.</param>
    /// <param name="otp">The OTP code to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    Task<bool> ValidateOtpAsync(string phoneNumber, Guid tenantId, string otp);
}

/// <summary>
/// Abstraction for sending OTP codes to members.
/// Implement with Twilio, WhatsApp Business API, etc.
/// </summary>
public interface IOtpSender
{
    /// <summary>
    /// Sends an OTP code to the specified phone number.
    /// </summary>
    Task SendOtpAsync(string phoneNumber, string otp);
}
