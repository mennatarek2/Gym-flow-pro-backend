namespace GMS.Core.Entities;

using GMS.Core.Constants;

/// <summary>
/// A reusable shift template (e.g. "Morning" 08:00-16:00). Not a POS cash-drawer <see cref="Shift"/> —
/// that is an unrelated, already-established concept (open/close/reconcile a cash drawer).
/// </summary>
public class EmployeeShift : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Local (Cairo) wall-clock start time.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Local (Cairo) wall-clock end time. EndTime &lt;= StartTime means the shift crosses midnight.</summary>
    public TimeOnly EndTime { get; set; }

    public int BreakMinutes { get; set; }
    public int GraceMinutes { get; set; }
    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; set; }
    public ICollection<EmployeeScheduleAssignment> Assignments { get; set; } = new List<EmployeeScheduleAssignment>();
}

/// <summary>
/// One employee assigned to one shift template on one calendar date. Unique per
/// (TenantId, EmployeeId, Date) — an employee can only have one shift per day, which is what makes
/// duplicate-assignment and overlap prevention trivial (no separate overlap check needed).
/// </summary>
public class EmployeeScheduleAssignment : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid EmployeeShiftId { get; set; }
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }

    public Employee? Employee { get; set; }
    public EmployeeShift? EmployeeShift { get; set; }
    public ICollection<EmployeeAttendance> Attendances { get; set; } = new List<EmployeeAttendance>();
}

/// <summary>
/// One employee's attendance for one calendar date. Separate from member <see cref="GymAttendance"/> —
/// that tracks members entering the gym, this tracks staff working hours. Unique per
/// (TenantId, EmployeeId, AttendanceDate): check-in and check-out both write to the same row, which is
/// what makes "double check-in" / "checkout without checkin" simple service-layer checks.
/// </summary>
public class EmployeeAttendance : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>Optional — attendance can exist without a schedule (e.g. an ad-hoc manual check-in
    /// for an employee with no shift assigned that day).</summary>
    public Guid? ScheduleId { get; set; }

    public DateOnly AttendanceDate { get; set; }
    public DateTime? CheckInAtUtc { get; set; }
    public DateTime? CheckOutAtUtc { get; set; }

    public int WorkedMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int OvertimeMinutes { get; set; }

    /// <summary>Present | Late | Absent | HalfDay | OnLeave — see <see cref="AttendanceStatuses"/>.</summary>
    public string Status { get; set; } = AttendanceStatuses.Present;

    /// <summary>Manual | Reception | Employee | Device — see <see cref="AttendanceSources"/>.</summary>
    public string Source { get; set; } = AttendanceSources.Manual;

    public string? Notes { get; set; }

    /// <summary>AppUser.Id of whoever recorded this (the manager/receptionist, or the employee themself
    /// for self-service). Null for legacy/system rows.</summary>
    public Guid? CreatedByAppUserId { get; set; }

    /// <summary>Set only for OnLeave placeholder rows created by approved-leave integration (Phase 4) —
    /// lets LeaveRequestService find and remove exactly the rows it created if the leave is later
    /// cancelled, without touching any row that has real check-in data.</summary>
    public Guid? LeaveRequestId { get; set; }

    public Employee? Employee { get; set; }
    public EmployeeScheduleAssignment? Schedule { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }
}
