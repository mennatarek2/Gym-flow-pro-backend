namespace GMS.Platform.DTOs;

public class PlatformUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool MfaEnabled { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class CreatePlatformUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    /// <summary>Set by the creating admin, same pattern as tenant-side CreateStaffRequest.Password.
    /// MfaEnabled starts false — the new user's own first login forces MFA setup, same as the seeded admin.</summary>
    public string Password { get; set; } = string.Empty;
}

public class ChangePlatformUserRoleRequest
{
    public string Role { get; set; } = string.Empty;
}
