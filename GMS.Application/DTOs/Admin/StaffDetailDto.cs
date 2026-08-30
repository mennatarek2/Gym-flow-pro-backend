namespace GMS.Application.DTOs.Admin;

/// <summary>
/// Full staff details DTO.
/// <see cref="Id"/> is ApplicationUser.Id (Identity / JWT sub), NOT AppUser.Id.
/// </summary>
public class StaffDetailDto
{
    public Guid Id { get; set; }
    /// <summary>AppUser.Id for this staff member, when an ops row exists for the tenant. Lets HR's
    /// Employee&lt;-&gt;Staff linking store the correct foreign key without a second lookup.</summary>
    public Guid? AppUserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public string? PhoneNumber { get; set; }
    public string? StaffNumber { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    public DateOnly? HireDate { get; set; }
    public string? Notes { get; set; }
}
