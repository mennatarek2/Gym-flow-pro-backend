namespace GMS.Core.Interfaces;

using System.Security.Claims;
using GMS.Core.Entities.Identity;

/// <summary>
/// Service for JWT access token generation and refresh token management.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a signed JWT access token containing user identity and tenant claims.
    /// Token lifetime: 15 minutes.
    /// </summary>
    /// <param name="user">The authenticated Identity user.</param>
    /// <param name="tenantId">Tenant ID to embed as a claim.</param>
    /// <param name="gymCode">Gym code to embed as a claim for tenant resolution.</param>
    /// <param name="roles">User roles to embed as claims.</param>
    /// <param name="permissions">Effective permission strings to embed as "perm" claims.</param>
    /// <returns>Signed JWT string.</returns>
    Task<string> GenerateAccessTokenAsync(
        ApplicationUser user,
        Guid tenantId,
        string gymCode,
        IList<string> roles,
        IEnumerable<string>? permissions = null);

    /// <summary>
    /// Short-lived (default 30 min) tenant JWT for platform support impersonation.
    /// Includes <c>impersonated_by_platform_user_id</c> and <c>token_use=tenant_impersonation</c>.
    /// Must not be paired with a refresh token.
    /// </summary>
    Task<string> GenerateImpersonationAccessTokenAsync(
        ApplicationUser user,
        Guid tenantId,
        string gymCode,
        IList<string> roles,
        IEnumerable<string>? permissions,
        Guid platformUserId,
        int lifetimeMinutes = 30);

    /// <summary>
    /// Generates a cryptographically secure random refresh token.
    /// </summary>
    /// <returns>Base64-encoded refresh token string.</returns>
    string GenerateRefreshToken();

    /// <summary>
    /// Validates an expired access token and extracts its claims.
    /// Used during token refresh to identify the user without requiring a valid token.
    /// </summary>
    /// <param name="token">The expired JWT access token.</param>
    /// <returns>ClaimsPrincipal if token structure is valid (ignoring expiry), null otherwise.</returns>
    ClaimsPrincipal? ValidateExpiredToken(string token);

    /// <summary>
    /// Computes SHA-256 hash of a token string for secure storage.
    /// </summary>
    string HashToken(string token);
}
