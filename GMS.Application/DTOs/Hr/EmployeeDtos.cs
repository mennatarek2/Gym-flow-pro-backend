namespace GMS.Application.DTOs.Hr;

public class EmployeeListItemDto
{
    public Guid Id { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? PhotoUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public Guid? PositionId { get; set; }
    public string? PositionName { get; set; }
    public bool HasLogin { get; set; }
    public DateOnly HireDate { get; set; }
}

public class EmployeeDto : EmployeeListItemDto
{
    public string? NationalId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public DateOnly? TerminationDate { get; set; }
    public Guid? AppUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    /// <summary>Populated when AppUserId is set — the linked Staff account's display info for the
    /// System Access tab. Null when not linked, even if AppUserId is set but the AppUser row is gone
    /// (orphan case), in which case Status reports "Missing" instead.</summary>
    public StaffAccountDto? StaffAccount { get; set; }
}

/// <summary>Linked Staff account summary shown on an Employee's System Access tab.</summary>
public class StaffAccountDto
{
    public Guid AppUserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Role { get; set; }
    /// <summary>Active | Disabled | Missing (AppUserId set but the account no longer resolves).</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>A Staff account eligible to be linked to an Employee (not already linked to a different one).</summary>
public class AvailableStaffDto
{
    public Guid AppUserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Role { get; set; }
    public bool IsActive { get; set; }
    public string? StaffNumber { get; set; }
}

public class LinkStaffRequest
{
    public Guid AppUserId { get; set; }
}

public class CreateEmployeeRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public DateOnly HireDate { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? PositionId { get; set; }
    /// <summary>Optional link to an existing staff login account (AppUser id). Leave null for employees with no login.</summary>
    public Guid? AppUserId { get; set; }
}

public class UpdateEmployeeRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? PositionId { get; set; }
    public Guid? AppUserId { get; set; }
    /// <summary>Active | Suspended — Terminated must go through the dedicated terminate action.</summary>
    public string Status { get; set; } = string.Empty;
}

public class TerminateEmployeeRequest
{
    public DateOnly TerminationDate { get; set; }
    public string? Notes { get; set; }
}
