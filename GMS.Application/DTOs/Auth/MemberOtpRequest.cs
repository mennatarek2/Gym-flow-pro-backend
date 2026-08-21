namespace GMS.Application.DTOs.Auth;

/// <summary>
/// Request DTO for sending an OTP to a member's phone number.
/// </summary>
public class MemberOtpRequest
{
    /// <summary>
    /// Member's phone number in international format (e.g. +201234567890).
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Gym code to identify which tenant this member belongs to.
    /// </summary>
    public string GymCode { get; set; } = string.Empty;
}
