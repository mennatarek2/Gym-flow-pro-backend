namespace GMS.Platform.DTOs;

/// <summary>Platform-facing request DTOs for P2.2 tenant staff management. Staff creation itself
/// reuses the existing GMS.Application.DTOs.Admin.CreateStaffRequest directly — no duplicate shape
/// needed there since it already only has what this surface needs (FullName/Email/Password/Role
/// plus optional profile fields the Platform Console simply won't send).</summary>
public class DisableTenantStaffRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class ReactivateTenantStaffRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class ChangeTenantStaffRoleRequest
{
    public string Role { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
