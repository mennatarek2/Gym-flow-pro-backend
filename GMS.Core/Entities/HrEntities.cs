namespace GMS.Core.Entities;

using GMS.Core.Constants;

/// <summary>HR org unit. Tenant-configurable, not a hard-coded enum.</summary>
public class Department : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; set; }
    public ICollection<Position> Positions { get; set; } = new List<Position>();
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}

/// <summary>HR job title/position. Tenant-configurable.</summary>
public class Position : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? Description { get; set; }
    /// <summary>Suggested/default basic salary for this position. Does not affect an employee's actual contract.</summary>
    public decimal? DefaultBasicSalary { get; set; }
    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; set; }
    public Department? Department { get; set; }
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}

/// <summary>
/// A gym employee. Distinct from <see cref="Identity.ApplicationUser"/>/<see cref="AppUser"/> —
/// an Employee is NOT required to have a login account. When one exists, <see cref="AppUserId"/>
/// links to the tenant-scoped <see cref="AppUser"/> row (never directly to ApplicationUser).
/// </summary>
public class Employee : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Tenant-scoped display number, e.g. EMP-0001. Server-generated. Never reused.</summary>
    public string EmployeeNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? PhotoUrl { get; set; }

    public DateOnly HireDate { get; set; }
    public DateOnly? TerminationDate { get; set; }

    /// <summary>Active | Suspended | Terminated — see <see cref="EmployeeStatuses"/>.</summary>
    public string Status { get; set; } = EmployeeStatuses.Active;

    public Guid? DepartmentId { get; set; }
    public Guid? PositionId { get; set; }

    /// <summary>
    /// Optional link to a <b>Staff desk</b> login (AppUser.Id — tenant-scoped ops row, not ApplicationUser.Id).
    /// Null means this employee has no Staff desk account. Independent of Employee App identity.
    /// </summary>
    public Guid? AppUserId { get; set; }

    /// <summary>
    /// Optional link to the <b>Employee App</b> mobile identity (AppUser.Id with Role=Employee).
    /// Set by employee-activate. Independent of Staff <see cref="AppUserId"/> — unlink-staff must not clear this.
    /// </summary>
    public Guid? EmployeeAppUserId { get; set; }

    public Tenant? Tenant { get; set; }
    public Department? Department { get; set; }
    public Position? Position { get; set; }
    public AppUser? AppUser { get; set; }
    public AppUser? EmployeeAppUser { get; set; }
    public ICollection<EmployeeContract> Contracts { get; set; } = new List<EmployeeContract>();
}

/// <summary>
/// One historical employment contract for an employee. Never mutated after creation except for
/// Status transitions — a new contract (e.g. a raise) is always a new row so salary history is preserved.
/// </summary>
public class EmployeeContract : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>Tenant-scoped display number, e.g. CT-0001. Server-generated. Never reused.</summary>
    public string ContractNumber { get; set; } = string.Empty;

    /// <summary>FullTime | PartTime | Temporary | Contract — see <see cref="EmploymentTypes"/>.</summary>
    public string EmploymentType { get; set; } = EmploymentTypes.FullTime;

    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal BasicSalary { get; set; }
    public int? WorkingHoursPerDay { get; set; }
    public int? WorkingDaysPerWeek { get; set; }

    /// <summary>Draft | Active | Ended — see <see cref="ContractStatuses"/>.</summary>
    public string Status { get; set; } = ContractStatuses.Draft;

    public string? Notes { get; set; }

    public Employee? Employee { get; set; }
}
