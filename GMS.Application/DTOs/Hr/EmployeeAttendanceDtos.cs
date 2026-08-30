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
