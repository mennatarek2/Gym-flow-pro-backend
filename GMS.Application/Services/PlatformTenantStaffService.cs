namespace GMS.Application.Services;

using GMS.Application.Common;
using GMS.Application.DTOs.Admin;
using GMS.Application.Interfaces;
using GMS.Platform.Interfaces;

/// <summary>
/// Thin platform-facing wrapper around <see cref="IAdminService"/> — see
/// <see cref="IPlatformTenantStaffService"/> for why this deliberately does not reimplement any
/// staff-management business logic.
/// </summary>
public class PlatformTenantStaffService : IPlatformTenantStaffService
{
    private readonly IAdminService _admin;
    private readonly IPlatformAuditService _platformAudit;

    public PlatformTenantStaffService(IAdminService admin, IPlatformAuditService platformAudit)
    {
        _admin = admin;
        _platformAudit = platformAudit;
    }

    public Task<Result<List<StaffListItemDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _admin.GetStaffUsersAsync(tenantId);

    public async Task<PlatformStaffMutationResult> CreateAsync(
        Guid tenantId,
        Guid platformActorId,
        CreateStaffRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var result = await _admin.CreateStaffUserAsync(tenantId, request);
        if (!result.IsSuccess)
        {
            var (code, message) = ParseError(result.Error);
            return PlatformStaffMutationResult.Fail(code, message);
        }

        await _platformAudit.LogAsync(
            platformActorId,
            "platform.tenant.staff_create",
            tenantId,
            before: null,
            after: new { staffId = result.Data!.Id, email = request.Email, role = result.Data!.Role },
            ipAddress);

        return PlatformStaffMutationResult.Ok(result.Data!);
    }

    public async Task<PlatformStaffMutationResult> DisableAsync(
        Guid tenantId,
        Guid staffId,
        Guid platformActorId,
        string reason,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var current = await _admin.GetStaffUserByIdAsync(tenantId, staffId);
        if (!current.IsSuccess)
            return PlatformStaffMutationResult.Fail("NOT_FOUND", current.Error ?? "Staff user not found.");

        var staff = current.Data!;
        if (!staff.IsActive)
            return PlatformStaffMutationResult.Fail("ALREADY_DISABLED", "This staff account is already disabled.");

        var result = await _admin.UpdateStaffUserAsync(tenantId, staffId, ToUpdateRequest(staff, role: null, isActive: false));
        if (!result.IsSuccess)
        {
            var (code, message) = ParseError(result.Error);
            return PlatformStaffMutationResult.Fail(code, message);
        }

        await _platformAudit.LogAsync(
            platformActorId,
            "platform.tenant.staff_disable",
            tenantId,
            before: new { staffId, isActive = true },
            after: new { staffId, isActive = false, reason },
            ipAddress);

        return PlatformStaffMutationResult.Ok(result.Data!);
    }

    public async Task<PlatformStaffMutationResult> ReactivateAsync(
        Guid tenantId,
        Guid staffId,
        Guid platformActorId,
        string reason,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var current = await _admin.GetStaffUserByIdAsync(tenantId, staffId);
        if (!current.IsSuccess)
            return PlatformStaffMutationResult.Fail("NOT_FOUND", current.Error ?? "Staff user not found.");

        var staff = current.Data!;
        if (staff.IsActive)
            return PlatformStaffMutationResult.Fail("ALREADY_ACTIVE", "This staff account is already active.");

        var result = await _admin.UpdateStaffUserAsync(tenantId, staffId, ToUpdateRequest(staff, role: null, isActive: true));
        if (!result.IsSuccess)
        {
            var (code, message) = ParseError(result.Error);
            return PlatformStaffMutationResult.Fail(code, message);
        }

        await _platformAudit.LogAsync(
            platformActorId,
            "platform.tenant.staff_reactivate",
            tenantId,
            before: new { staffId, isActive = false },
            after: new { staffId, isActive = true, reason },
            ipAddress);

        return PlatformStaffMutationResult.Ok(result.Data!);
    }

    public async Task<PlatformStaffMutationResult> ChangeRoleAsync(
        Guid tenantId,
        Guid staffId,
        string newRole,
        Guid platformActorId,
        string reason,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var current = await _admin.GetStaffUserByIdAsync(tenantId, staffId);
        if (!current.IsSuccess)
            return PlatformStaffMutationResult.Fail("NOT_FOUND", current.Error ?? "Staff user not found.");

        var staff = current.Data!;
        if (string.Equals(staff.Role, newRole, StringComparison.OrdinalIgnoreCase))
            return PlatformStaffMutationResult.Fail("NO_CHANGE", $"{staff.FullName} already has the {newRole} role.");

        var oldRole = staff.Role;
        var result = await _admin.UpdateStaffUserAsync(tenantId, staffId, ToUpdateRequest(staff, role: newRole, isActive: staff.IsActive));
        if (!result.IsSuccess)
        {
            var (code, message) = ParseError(result.Error);
            return PlatformStaffMutationResult.Fail(code, message);
        }

        await _platformAudit.LogAsync(
            platformActorId,
            "platform.tenant.staff_role_change",
            tenantId,
            before: new { staffId, role = oldRole },
            after: new { staffId, role = newRole, reason },
            ipAddress);

        return PlatformStaffMutationResult.Ok(result.Data!);
    }

    /// <summary>
    /// AdminService.UpdateStaffUserAsync takes a full replacement request (FullName/IsActive are
    /// always applied, never null-guarded) — so every call here re-sends the staff member's current
    /// values for anything not being changed, to avoid silently blanking fields. A null/empty Role
    /// tells AdminService "don't change the role" (its own IsNullOrWhiteSpace check).
    /// </summary>
    private static UpdateStaffRequest ToUpdateRequest(StaffDetailDto staff, string? role, bool isActive) => new()
    {
        FullName = staff.FullName,
        Role = role ?? string.Empty,
        IsActive = isActive,
        PhoneNumber = staff.PhoneNumber,
        JobTitle = staff.JobTitle,
        Department = staff.Department,
        HireDate = staff.HireDate,
        Notes = staff.Notes
    };

    /// <summary>
    /// AdminService/AdminController already establish this "CODE|message" convention for
    /// OWNER_PROTECTED and PLAN_LIMIT_EXCEEDED — reused verbatim rather than inventing a new
    /// error shape for the platform-facing surface.
    /// </summary>
    private static (string Code, string Message) ParseError(string? error)
    {
        if (string.IsNullOrEmpty(error))
            return ("BAD_REQUEST", "Request failed.");

        var pipeIndex = error.IndexOf('|');
        if (pipeIndex > 0)
            return (error[..pipeIndex], error[(pipeIndex + 1)..]);

        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return ("NOT_FOUND", error);

        return ("BAD_REQUEST", error);
    }
}
