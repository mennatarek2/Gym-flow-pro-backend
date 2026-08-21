namespace GMS.Core.Entities.Identity;

using Microsoft.AspNetCore.Identity;

/// <summary>
/// ASP.NET Core Identity user for authentication.
/// Stored separately from the domain AppUser entity.
/// AppUser.UserId references ApplicationUser.Id for linking.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    // Tenant context
    public Guid TenantId { get; set; }

    // Profile
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string ProfilePhotoUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Per-user permission grant/revoke overrides as JSON, layered on top of the role-based
    /// defaults from <see cref="GMS.Core.Interfaces.IPermissionProvider"/>. Reserved for future
    /// use — currently read but not applied.
    /// </summary>
    public string? PermissionsOverride { get; set; }

    // Timestamps
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
