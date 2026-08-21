namespace GMS.Application.DTOs.Attendance;

/// <summary>
/// Desk barcode check-in — exact MemberNumber from an access card (MAC-P0 / C7).
/// </summary>
public class BarcodeCheckinRequest
{
    /// <summary>Scanned MemberNumber (e.g. GYM-042). Exact match required.</summary>
    public string Code { get; set; } = string.Empty;
}
