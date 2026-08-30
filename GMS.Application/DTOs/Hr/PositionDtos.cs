namespace GMS.Application.DTOs.Hr;

public class PositionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? Description { get; set; }
    public decimal? DefaultBasicSalary { get; set; }
    public bool IsActive { get; set; }
    public int EmployeeCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class CreatePositionRequest
{
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? Description { get; set; }
    public decimal? DefaultBasicSalary { get; set; }
}

public class UpdatePositionRequest
{
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? Description { get; set; }
    public decimal? DefaultBasicSalary { get; set; }
    public bool IsActive { get; set; } = true;
}
