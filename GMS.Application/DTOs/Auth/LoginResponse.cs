namespace GMS.Application.DTOs.Auth;

/// <summary>
/// Response DTO for successful authentication.
/// Contains JWT tokens and basic user information.
/// </summary>
public class LoginResponse
{
    /// <summary>
    /// Short-lived JWT access token (15 minutes).
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Long-lived refresh token (30 days) for obtaining new access tokens.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// UTC expiration time of the access token.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Basic user profile information.
    /// </summary>
    public UserInfo User { get; set; } = new();
}

/// <summary>
/// Nested user information included in auth responses.
/// </summary>
public class UserInfo
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string GymCode { get; set; } = string.Empty;
}
