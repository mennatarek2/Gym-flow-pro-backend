namespace GMS.Application.DTOs.Admin;

/// <summary>
/// Lightweight DTO for staff list endpoints.
/// <see cref="Id"/> is ApplicationUser.Id (Identity / JWT sub), NOT AppUser.Id.
/// <see cref="Role"/> is the canonical PascalCase Identity role name.
/// <see cref="LastLoginAt"/> is AppUser.LastLoginAtUtc (null = never logged in).
/// </summary>
public class StaffListItemDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public string? PhoneNumber { get; set; }
    public string? StaffNumber { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    public DateOnly? HireDate { get; set; }
}
