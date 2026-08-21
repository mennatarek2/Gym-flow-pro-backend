namespace GMS.Application.DTOs.Attendance;

/// <summary>
/// Derived live occupancy. Not a second attendance ledger.
/// CurrentlyInside = today's visits with CheckOutAtUtc null (same window as GET /attendance/today).
/// Status (Available / Busy / Full) is a presentation concern — not returned here.
/// </summary>
public class GymOccupancyDto
{
    public string GymName { get; set; } = string.Empty;
    public string GymNameAr { get; set; } = string.Empty;
    public bool GymActive { get; set; }
    public int? MaxCapacity { get; set; }
    public int CurrentlyInside { get; set; }
    public int? Available { get; set; }
    public int? OccupancyPercent { get; set; }
    public string Source { get; set; } = "attendance_open_visits";
}
