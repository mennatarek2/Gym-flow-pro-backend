namespace GMS.Application.DTOs.Hr;

public class EmployeeAttendanceDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public Guid? ScheduleId { get; set; }
    public string? EmployeeShiftName { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public DateTime? CheckInAtUtc { get; set; }
    public DateTime? CheckOutAtUtc { get; set; }
    public int WorkedMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class CheckInRequest
{
    /// <summary>Omitted on /me self-service routes, where the caller's own employee id is resolved from identity.</summary>
    public Guid? EmployeeId { get; set; }
    public string? Notes { get; set; }
}

public class CheckOutRequest
{
    public Guid? EmployeeId { get; set; }
}

public class CorrectAttendanceRequest
{
    public string? Status { get; set; }
    public DateTime? CheckInAtUtc { get; set; }
    public DateTime? CheckOutAtUtc { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Body for both /me/qr-validate and /me/qr-check-in — the raw text decoded from the gym's
/// QR image (a short-lived signed token, see <see cref="GMS.Application.Interfaces.IGymQrTokenService"/>).</summary>
public class EmployeeQrRequest
{
    public string QrToken { get; set; } = string.Empty;
}

/// <summary>
/// Read-only preview returned by /me/qr-validate — lets the app show a confirmation screen
/// (shift, computed on-time/late preview) before the employee taps Confirm. Never persisted;
/// the actual attendance row is written by /me/qr-check-in, which independently recomputes
/// everything at that moment (this preview cannot be replayed to manufacture attendance data).
/// </summary>
public class EmployeeQrCheckinPreviewDto
{
    public bool HasSchedule { get; set; }
    public string? ShiftName { get; set; }
    public TimeOnly? ShiftStart { get; set; }
    public TimeOnly? ShiftEnd { get; set; }

    /// <summary>Server clock at validation time — the actual check-in time will be a few seconds later.</summary>
    public DateTime PreviewCheckInAtUtc { get; set; }
    public int PreviewLateMinutes { get; set; }

    /// <summary>Present | Late — see <see cref="GMS.Core.Constants.AttendanceStatuses"/>.</summary>
    public string PreviewStatus { get; set; } = string.Empty;
}
