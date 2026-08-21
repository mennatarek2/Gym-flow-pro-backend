namespace GMS.Core.Entities;

public class ActivitySession : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid ActivityId { get; set; }
    public Guid? ScheduleId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Capacity { get; set; }
    public Guid? CoachUserId { get; set; }
    public string Status { get; set; } = "upcoming";

    public Activity? Activity { get; set; }
    public ActivitySchedule? Schedule { get; set; }
    public AppUser? CoachUser { get; set; }
    public ICollection<ActivityBooking> Bookings { get; set; } = new List<ActivityBooking>();
}
