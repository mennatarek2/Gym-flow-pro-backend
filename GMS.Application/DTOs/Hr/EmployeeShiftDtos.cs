namespace GMS.Application.DTOs.Hr;

public class EmployeeShiftDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public int GraceMinutes { get; set; }
    public bool IsActive { get; set; }
    public bool CrossesMidnight { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class CreateEmployeeShiftRequest
{
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public int GraceMinutes { get; set; }
}

public class UpdateEmployeeShiftRequest
{
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public int GraceMinutes { get; set; }
    public bool IsActive { get; set; } = true;
}
