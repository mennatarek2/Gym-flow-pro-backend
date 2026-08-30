namespace GMS.Application.DTOs.Hr;

public class EmployeeScheduleAssignmentDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public Guid EmployeeShiftId { get; set; }
    public string EmployeeShiftName { get; set; } = string.Empty;
    public TimeOnly ShiftStartTime { get; set; }
    public TimeOnly ShiftEndTime { get; set; }
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }
}

public class AssignScheduleRequest
{
    public Guid EmployeeId { get; set; }
    public Guid EmployeeShiftId { get; set; }
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }
}

public class BulkAssignScheduleRequest
{
    public List<Guid> EmployeeIds { get; set; } = new();
    public Guid EmployeeShiftId { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public string? Notes { get; set; }
}

public class BulkAssignResultCellDto
{
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public bool Success { get; set; }
    public string? SkipReason { get; set; }
}

public class BulkAssignResultDto
{
    public int AssignedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<BulkAssignResultCellDto> Cells { get; set; } = new();
}
