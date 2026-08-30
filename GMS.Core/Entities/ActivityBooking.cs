namespace GMS.Core.Entities;

public class ActivityBooking : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public Guid? MemberId { get; set; }
    /// <summary>Required when this is an anonymous walk-in booking.</summary>
    public string? GuestName { get; set; }
    public string? GuestPhone { get; set; }
    public string Status { get; set; } = "booked";
    public string Source { get; set; } = "staff";
    public Guid? CoveringMembershipId { get; set; }
    public Guid? SaleId { get; set; }
    public Guid? AttendanceId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? CheckedInAtUtc { get; set; }
    public Guid? CheckedInByUserId { get; set; }

    public ActivitySession? Session { get; set; }
    public GymMember? Member { get; set; }
    public Membership? CoveringMembership { get; set; }
    public Sale? Sale { get; set; }
    public GymAttendance? Attendance { get; set; }
    public AppUser? CheckedInByUser { get; set; }
}
