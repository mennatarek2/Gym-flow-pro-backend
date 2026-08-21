namespace GMS.Application.DTOs.Activities;

public class ActivityScheduleDto
{
    public Guid Id { get; set; }
    public Guid ActivityId { get; set; }
    public string DaysOfWeek { get; set; } = "[]";
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int Capacity { get; set; }
    public Guid? CoachUserId { get; set; }
    public string? CoachName { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public bool IsActive { get; set; }
}

public class CreateScheduleRequest
{
    public List<int> DaysOfWeek { get; set; } = new();
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public int? Capacity { get; set; }
    public Guid? CoachUserId { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
}
