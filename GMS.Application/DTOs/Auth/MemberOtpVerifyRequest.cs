namespace GMS.Application.DTOs.Auth;

/// <summary>
/// Request DTO for verifying an OTP code and obtaining a member JWT.
/// </summary>
public class MemberOtpVerifyRequest
{
    /// <summary>
    /// Member's phone number (must match the number the OTP was sent to).
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Gym code identifying the tenant.
    /// </summary>
    public string GymCode { get; set; } = string.Empty;

    /// <summary>
    /// The 6-digit OTP code received via SMS/WhatsApp.
    /// </summary>
    public string Otp { get; set; } = string.Empty;
}
