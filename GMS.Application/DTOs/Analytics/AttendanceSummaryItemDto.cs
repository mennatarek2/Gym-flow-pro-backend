namespace GMS.Application.DTOs.Analytics;

/// <summary>
/// Attendance summary item for reports.
/// </summary>
public class AttendanceSummaryItemDto
{
    public DateOnly Date { get; set; }
    public int CheckinCount { get; set; }
    public int UniqueMembers { get; set; }
}
