namespace GMS.Core.Entities;

/// <summary>
/// Represents a member's gym attendance (check-in/check-out).
/// Tracks when members visit the gym and entry method.
/// </summary>
public class GymAttendance : BaseEntity
{
    // Tenant context
    public Guid TenantId { get; set; }

    // Foreign keys
    public Guid? MemberId { get; set; }
    /// <summary>Snapshot identity when a guest walk-in checks into a class.</summary>
    public string? GuestName { get; set; }
    public string? GuestPhone { get; set; }
    public Guid? MembershipId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? StaffUserId { get; set; }

    // Check-in/out
    public DateTime CheckInAtUtc { get; set; }
    public DateTime? CheckOutAtUtc { get; set; }

    /// <summary>
    /// Cairo calendar date of <see cref="CheckInAtUtc"/>, auto-stamped by GymFlowProDbContext on
    /// insert (never set by callers). Backs the unique gym-floor-check-in-per-member-per-day
    /// constraint (see GymAttendanceConfiguration) — scoped to SessionId == null rows so a class/
    /// session booking check-in (which legitimately repeats per session) never collides with it.
    /// </summary>
    public DateOnly AttendanceDateCairo { get; set; }

    // Entry method
    public string EntryMethod { get; set; } = "qr";
    // Valid values: 'qr', 'manual'

    // Manual entry reason (if applicable)
    public string? ManualReason { get; set; }

    // Duration (calculated)
    public TimeSpan? Duration { get; set; }

    public string? PresenceStatus { get; set; }
    public string? DeviceFingerprint { get; set; }

    // Timestamps
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public GymMember? Member { get; set; }
    public Membership? Membership { get; set; }
    public AppUser? StaffUser { get; set; }
    public ActivityBooking? Booking { get; set; }
    public ActivitySession? Session { get; set; }
}
