namespace GMS.Core.Entities.Identity;

/// <summary>
/// Represents a refresh token for JWT token rotation.
/// Stored in DB for validation and revocation support.
/// Supports sliding rotation: each use generates a new token and revokes the old one.
/// </summary>
public class RefreshToken : BaseEntity
{
    /// <summary>
    /// SHA-256 hash of the actual token value.
    /// The raw token is only sent to the client; we store the hash for lookup.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// The ApplicationUser this token belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Tenant context for multi-tenant isolation.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// When this refresh token expires (30 days from creation).
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// When the token was revoked (null if still active).
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// If this token was rotated, the hash of the replacement token.
    /// Used for detecting reuse of old tokens (potential theft).
    /// </summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>
    /// IP address that created this token.
    /// </summary>
    public string? CreatedByIp { get; set; }

    // Computed
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
    public bool IsRevoked => RevokedAtUtc != null;
    public bool IsActive => !IsRevoked && !IsExpired;

    // Navigation
    public ApplicationUser? User { get; set; }
}
