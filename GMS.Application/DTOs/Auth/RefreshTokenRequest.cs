namespace GMS.Application.DTOs.Auth;

/// <summary>
/// Request DTO for refreshing an expired access token.
/// </summary>
public class RefreshTokenRequest
{
    /// <summary>
    /// The refresh token obtained from login or previous refresh.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;
}
