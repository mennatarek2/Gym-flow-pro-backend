namespace GMS.Application.DTOs.Attendance;

/// <summary>
/// Request DTO for QR code check-in.
/// Member scans the gym's displayed QR, which now encodes a short-lived signed token (see
/// <see cref="GMS.Application.Interfaces.IGymQrTokenService"/>) rather than a permanent gym code —
/// the field is still named GymCode for wire compatibility with already-shipped Flutter clients
/// that just forward whatever string the camera decoded. The member's identity comes from the JWT.
/// </summary>
public class QrCheckinRequest
{
    /// <summary>
    /// Raw text decoded from the gym's QR image — the signed, short-lived token minted by
    /// GenerateQrTokenAsync (NOT a literal "GYM-CAIRO-01" style code).
    /// </summary>
    public string GymCode { get; set; } = string.Empty;
}
