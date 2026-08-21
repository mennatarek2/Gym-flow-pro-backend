using GMS.Core.Entities;

namespace GMS.Application.Interfaces;

/// <summary>
/// Strategy interface for delivering OTP codes to members.
/// Implementations handle email or SMS delivery, masking the destination address.
/// </summary>
public interface IOtpDeliveryStrategy
{
    /// <summary>
    /// Sends a generated OTP to the member via configured provider (email, SMS, etc.).
    /// Returns a masked destination string (e.g., "ah***@gmail.com") for user feedback.
    /// </summary>
    /// <param name="member">The member to send OTP to.</param>
    /// <param name="otp">The generated OTP code (e.g., "123456").</param>
    /// <param name="gymName">The gym name (for personalization in the message).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Masked destination (e.g., "ah***@gmail.com" for email).</returns>
    /// <exception cref="InvalidOperationException">Thrown if member has no email address on file.</exception>
    Task<string> SendOtpAsync(GymMember member, string otp, string gymName, CancellationToken cancellationToken = default);
}
