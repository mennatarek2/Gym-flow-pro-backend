namespace GMS.Application.DTOs.Hr;

public class LeaveBalanceDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string LeaveType { get; set; } = string.Empty;
    public int Year { get; set; }
    public decimal EntitledDays { get; set; }
    public decimal UsedDays { get; set; }
    public decimal RemainingDays { get; set; }
}

public class LeaveRequestDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string LeaveType { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal DurationDays { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public Guid? ReviewedByAppUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNotes { get; set; }
}

public class CreateLeaveRequestRequest
{
    public string LeaveType { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>Only meaningful (and required) for Permission-type leave, where StartDate == EndDate
    /// and this is a fraction of a day (e.g. 0.25). Ignored for every other leave type, whose duration
    /// is always the whole-day count between StartDate and EndDate inclusive.</summary>
    public decimal? DurationDays { get; set; }

    public string? Reason { get; set; }
}
