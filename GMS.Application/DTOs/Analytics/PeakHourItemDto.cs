namespace GMS.Application.DTOs.Analytics;

/// <summary>
/// Peak hours analysis showing busiest times.
/// </summary>
public class PeakHourItemDto
{
    public string TimeSlot { get; set; } = string.Empty; // "10:00-11:00"
    public int CheckinCount { get; set; }
    public decimal Percentage { get; set; }
}
