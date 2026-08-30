namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

/// <summary>Manages PlatformAdminUser accounts (platform_support/platform_ops/platform_admin) —
/// entirely separate from tenant Staff management. Every mutation is PlatformAdminOnly and audited.</summary>
public interface IPlatformUserAdminService
{
    Task<IReadOnlyList<PlatformUserDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<(PlatformActionResult Result, PlatformUserDto? User)> CreateAsync(
        Guid actorPlatformUserId, CreatePlatformUserRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Fails with SELF_PROTECTED if targetId == actorPlatformUserId — an admin can never
    /// disable their own account through this API (mirrors the tenant-side OWNER_PROTECTED rule).</summary>
    Task<(PlatformActionResult Result, PlatformUserDto? User)> DisableAsync(
        Guid actorPlatformUserId, Guid targetId, string? ipAddress, CancellationToken cancellationToken = default);

    Task<(PlatformActionResult Result, PlatformUserDto? User)> ReactivateAsync(
        Guid actorPlatformUserId, Guid targetId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Fails with SELF_PROTECTED if targetId == actorPlatformUserId — prevents an admin from
    /// accidentally demoting themselves out of the role that let them make the change.</summary>
    Task<(PlatformActionResult Result, PlatformUserDto? User)> ChangeRoleAsync(
        Guid actorPlatformUserId, Guid targetId, string newRole, string? ipAddress, CancellationToken cancellationToken = default);
}
