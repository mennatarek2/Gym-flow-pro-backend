namespace GMS.Core.Entities;

using GMS.Core.Constants;

/// <summary>
/// A single leave request. Duration is measured in days (fractional for Permission-type leave,
/// e.g. 0.5 for a half-day). Approval consumes the employee's LeaveBalance for that leave
/// type/year; cancelling a previously-approved request restores it. See LeaveRequestService for
/// the full lifecycle and its EmployeeAttendance integration.
/// </summary>
public class LeaveRequest : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>Annual | Sick | Emergency | Unpaid | Permission — see <see cref="LeaveTypes"/>.</summary>
    public string LeaveType { get; set; } = LeaveTypes.Annual;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>Whole days for every type except Permission, which may be a fraction of one day
    /// (StartDate == EndDate required for Permission).</summary>
    public decimal DurationDays { get; set; }

    public string? Reason { get; set; }

    /// <summary>Pending | Approved | Rejected | Cancelled — see <see cref="LeaveRequestStatuses"/>.</summary>
    public string Status { get; set; } = LeaveRequestStatuses.Pending;

    public DateTime RequestedAtUtc { get; set; }
    public Guid? ReviewedByAppUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNotes { get; set; }

    public Employee? Employee { get; set; }
}

/// <summary>
/// An employee's remaining allowance for one leave type in one calendar year. Auto-created on
/// first use with <see cref="LeaveTypes.DefaultEntitlementDays"/>; owners/managers can override
/// EntitledDays per employee via LeaveBalanceService.SetEntitlementAsync. Unpaid leave never gets
/// a balance row (it doesn't consume one).
/// </summary>
public class LeaveBalance : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string LeaveType { get; set; } = LeaveTypes.Annual;
    public int Year { get; set; }
    public decimal EntitledDays { get; set; }
    public decimal UsedDays { get; set; }

    public Employee? Employee { get; set; }
}
