namespace GMS.Application.DTOs.Attendance;

/// <summary>
/// A freshly minted, short-lived gym QR token for display at reception. Carries no member/employee
/// identity — anyone who scans it while valid still goes through the full check-in validation gauntlet.
/// </summary>
public class GymQrTokenDto
{
    /// <summary>Opaque signed token — this is the exact string that should be encoded into the QR image.</summary>
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int ExpiresInSeconds { get; set; }
}
