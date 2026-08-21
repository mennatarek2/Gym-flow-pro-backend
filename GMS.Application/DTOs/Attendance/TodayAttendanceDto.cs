namespace GMS.Application.DTOs.Attendance;

/// <summary>
/// Today's attendance record for the live dashboard.
/// </summary>
public class TodayAttendanceDto
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public string MemberNumber { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    public string MemberNameAr { get; set; } = string.Empty;
    public DateTime CheckInAtUtc { get; set; }
    public DateTime? CheckOutAtUtc { get; set; }
    public string EntryMethod { get; set; } = string.Empty;
    public string? PlanName { get; set; }
}
