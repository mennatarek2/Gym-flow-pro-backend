namespace GMS.Application.Services;

using GMS.Core.Attributes;
using GMS.Core.Entities;

/// <summary>
/// Named audit snapshot for <see cref="Employee"/> — a concrete class (not an anonymous object) is
/// required for <see cref="RedactAttribute"/> to take effect, since AuditService.SerializeRedacted
/// reflects over real declared properties.
/// </summary>
internal sealed class EmployeeAuditSnapshot
{
    public string EmployeeNumber { get; }
    public string FirstName { get; }
    public string LastName { get; }
    [Redact] public string? Phone { get; }
    [Redact] public string? Email { get; }
    [Redact] public string? NationalId { get; }
    public string Status { get; }
    public Guid? DepartmentId { get; }
    public Guid? PositionId { get; }
    public Guid? AppUserId { get; }

    public EmployeeAuditSnapshot(Employee employee)
    {
        EmployeeNumber = employee.EmployeeNumber;
        FirstName = employee.FirstName;
        LastName = employee.LastName;
        Phone = employee.Phone;
        Email = employee.Email;
        NationalId = employee.NationalId;
        Status = employee.Status;
        DepartmentId = employee.DepartmentId;
        PositionId = employee.PositionId;
        AppUserId = employee.AppUserId;
    }
}
