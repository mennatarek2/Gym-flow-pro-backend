namespace GMS.Application.DTOs.Auth;

/// <summary>
/// Request DTO for staff login (email + password).
/// </summary>
public class LoginRequest
{
    /// <summary>
    /// User's email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// User's password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gym code to identify which tenant to authenticate against.
    /// </summary>
    public string GymCode { get; set; } = string.Empty;
}
