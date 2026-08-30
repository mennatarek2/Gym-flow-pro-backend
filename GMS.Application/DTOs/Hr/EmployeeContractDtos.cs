namespace GMS.Application.DTOs.Hr;

public class EmployeeContractDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string EmploymentType { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal BasicSalary { get; set; }
    public int? WorkingHoursPerDay { get; set; }
    public int? WorkingDaysPerWeek { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class CreateEmployeeContractRequest
{
    public string EmploymentType { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal BasicSalary { get; set; }
    public int? WorkingHoursPerDay { get; set; }
    public int? WorkingDaysPerWeek { get; set; }
    /// <summary>Draft | Active — defaults to Active when omitted.</summary>
    public string? Status { get; set; }
    public string? Notes { get; set; }
}
