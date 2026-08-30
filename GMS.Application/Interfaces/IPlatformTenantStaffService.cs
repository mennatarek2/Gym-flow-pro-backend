namespace GMS.Application.Interfaces;

using GMS.Application.DTOs.Admin;
using GMS.Application.Common;

/// <summary>
/// Platform-side bridge onto the existing tenant staff management domain (<see cref="IAdminService"/>).
/// Every method here delegates the actual mutation — including Owner protection, role validation,
/// tenant-scoped lookup, and the tenant's own <c>staff.*</c> audit trail — to <see cref="IAdminService"/>
/// unchanged. This service only adds what is specific to being invoked from the Platform Console:
/// an explicit platform actor id, a mandatory audit reason for destructive/change actions, and a
/// mirrored entry in the platform-wide audit log (<c>platform.tenant.staff_*</c>), matching every
/// other platform-initiated tenant mutation (force-suspend, feature overrides, etc.).
/// </summary>
public interface IPlatformTenantStaffService
{
    Task<Result<List<StaffListItemDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<PlatformStaffMutationResult> CreateAsync(
        Guid tenantId,
        Guid platformActorId,
        CreateStaffRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<PlatformStaffMutationResult> DisableAsync(
        Guid tenantId,
        Guid staffId,
        Guid platformActorId,
        string reason,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<PlatformStaffMutationResult> ReactivateAsync(
        Guid tenantId,
        Guid staffId,
        Guid platformActorId,
        string reason,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<PlatformStaffMutationResult> ChangeRoleAsync(
        Guid tenantId,
        Guid staffId,
        string newRole,
        Guid platformActorId,
        string reason,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}

public class PlatformStaffMutationResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public StaffDetailDto? Staff { get; set; }

    public static PlatformStaffMutationResult Ok(StaffDetailDto staff) => new()
    {
        Success = true,
        Staff = staff
    };

    public static PlatformStaffMutationResult Fail(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message
    };
}
