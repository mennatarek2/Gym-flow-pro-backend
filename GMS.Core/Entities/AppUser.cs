namespace GMS.Core.Entities;

/// <summary>
/// Represents an application user (staff member or admin) in the system.
/// Each user belongs to a specific tenant.
/// </summary>
public class AppUser : BaseEntity
{
    // Tenant context
    public Guid TenantId { get; set; }

    // User information
    public string UserId { get; set; } = string.Empty; // Azure AD / Identity ID
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    // Profile
    public string ProfilePhotoUrl { get; set; } = string.Empty;

    // Roles & Permissions
    public string Role { get; set; } = "staff"; // 'admin', 'manager', 'trainer', 'staff', 'member'
    public bool IsActive { get; set; } = true;

    // Access Control
    public DateTime? LastLoginAtUtc { get; set; }
    public bool TwoFactorEnabled { get; set; } = false;

    /// <summary>Tenant-scoped display number, e.g. ST-0001. Server-generated. Never reused.</summary>
    public string? StaffNumber { get; set; }

    /// <summary>Operational job label. Does not replace <see cref="Role"/>.</summary>
    public string? JobTitle { get; set; }

    /// <summary>One of <c>StaffDepartments</c>. Not an org-chart entity.</summary>
    public string? Department { get; set; }

    public DateOnly? HireDate { get; set; }

    public string? Notes { get; set; }

    // Timestamps
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public GymMember? LinkedMember { get; set; }
    public ICollection<GymAttendance> RecordedAttendances { get; set; } = new List<GymAttendance>();
}
