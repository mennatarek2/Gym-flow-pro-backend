namespace GMS.Application.DTOs.Attendance;

/// <summary>
/// Response DTO for successful check-in (QR or manual).
/// </summary>
public class QrCheckinResponse
{
    public Guid AttendanceId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberNameAr { get; set; } = string.Empty;
    public DateTime CheckInAtUtc { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanNameAr { get; set; } = string.Empty;
    public int? SessionsRemaining { get; set; }
    public string Message { get; set; } = string.Empty;
    public string MessageAr { get; set; } = string.Empty;
}
