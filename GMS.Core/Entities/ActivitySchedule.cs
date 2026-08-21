namespace GMS.Core.Entities;

public class ActivitySchedule : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid ActivityId { get; set; }
    public string DaysOfWeek { get; set; } = "[]";
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int Capacity { get; set; }
    public Guid? CoachUserId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public bool IsActive { get; set; } = true;

    public Activity? Activity { get; set; }
    public AppUser? CoachUser { get; set; }
}
