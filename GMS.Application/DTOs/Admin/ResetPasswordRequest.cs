namespace GMS.Application.DTOs.Admin;

/// <summary>
/// Request DTO for resetting staff password.
/// </summary>
public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}
