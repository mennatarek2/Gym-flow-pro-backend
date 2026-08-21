namespace GMS.Application.DTOs.Attendance;

/// <summary>
/// Request DTO for QR code check-in.
/// Member scans the gym's static QR which contains the gymCode.
/// The member's identity comes from the JWT token.
/// </summary>
public class QrCheckinRequest
{
    /// <summary>
    /// Gym code extracted from the static QR code (e.g. "GYM-CAIRO-01").
    /// </summary>
    public string GymCode { get; set; } = string.Empty;
}
