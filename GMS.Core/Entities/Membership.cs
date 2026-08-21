namespace GMS.Core.Entities;

/// <summary>
/// Represents a member's active or inactive membership to a plan.
/// Tracks membership lifecycle: active, expired, frozen, cancelled.
/// </summary>
public class Membership : BaseEntity
{
    // Tenant context
    public Guid TenantId { get; set; }

    // Foreign keys
    public Guid MemberId { get; set; }
    public Guid PlanId { get; set; }

    // Membership dates
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    // Status
    public string Status { get; set; } = "active";
    // Valid values: 'active', 'expired', 'frozen', 'cancelled'

    // Sessions (for session-based plans)
    public int? SessionsRemaining { get; set; }

    // Freeze tracking
    public DateOnly? FrozenFromDate { get; set; }
    public DateOnly? FrozenUntilDate { get; set; }

    // Payment
    public string PaymentMethod { get; set; } = "cash";
    public decimal AmountPaid { get; set; }
    public DateTime? PaymentDate { get; set; }

    // Auto-renewal
    public bool AutoRenew { get; set; } = false;
    public DateTime? LastRenewalDate { get; set; }

    /// <summary>
    /// Desk renew transition: cancel_and_switch | queue_next | manual_rollover.
    /// Null for assign / legacy rows.
    /// </summary>
    public string? PlanTransitionMode { get; set; }

    // Timestamps
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public GymMember? Member { get; set; }
    public MembershipPlan? Plan { get; set; }
    public ICollection<GymAttendance> Attendances { get; set; } = new List<GymAttendance>();
}
