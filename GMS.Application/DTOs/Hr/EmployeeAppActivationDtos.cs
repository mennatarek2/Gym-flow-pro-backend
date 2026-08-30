namespace GMS.Application.DTOs.Hr;

public class EmployeeAppActivationCodeResponse
{
    public Guid EmployeeId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string ActivationCode { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int ExpiresInMinutes { get; set; }
}

/// <summary>Authenticated Employee App profile — never invents fields beyond the Employee model.</summary>
public class EmployeeMeDto
{
    public Guid Id { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? PhotoUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public Guid? PositionId { get; set; }
    public string? PositionName { get; set; }
    public DateOnly HireDate { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
