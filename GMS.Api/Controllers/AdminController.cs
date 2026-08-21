namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Filters;
using GMS.Application.DTOs.Admin;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>
/// Admin controller for staff user management.
/// All operations scoped to current tenant.
/// Only Owner can manage staff.
/// </summary>
[Route("api/admin")]
[Authorize(Policy = "OwnerOnly")]
public class AdminController : BaseApiController
{
    private readonly IAdminService _adminService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IAdminService adminService,
        ITenantContext tenantContext,
        ILogger<AdminController> logger)
    {
        _adminService = adminService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all staff users for current tenant (includes Owner; excludes Member).
    /// GET /api/admin/staff. Id is ApplicationUser.Id.
    /// </summary>
    [HttpGet("staff")]
    [ProducesResponseType(typeof(List<StaffListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStaffUsers()
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _adminService.GetStaffUsersAsync(tenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error!);
    }

    /// <summary>
    /// Get specific staff user details.
    /// GET /api/admin/staff/{id}
    /// </summary>
    [HttpGet("staff/{id:guid}")]
    [ProducesResponseType(typeof(StaffDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStaffUser(Guid id)
    {
        var result = await _adminService.GetStaffUserByIdAsync(_tenantContext.TenantId, id);
        return result.IsSuccess ? Ok(result.Data) : NotFound(result.Error!);
    }

    /// <summary>
    /// Create a new staff user (Manager, Trainer, or Receptionist — not Owner).
    /// POST /api/admin/staff
    /// </summary>
    [HttpPost("staff")]
    [ProducesResponseType(typeof(StaffDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateStaffUser([FromBody] CreateStaffRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _adminService.CreateStaffUserAsync(tenantId, request);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to create staff user: {Error}", result.Error);
            if (result.Error?.StartsWith("PLAN_LIMIT_EXCEEDED|", StringComparison.Ordinal) == true)
            {
                var detail = result.Error["PLAN_LIMIT_EXCEEDED|".Length..];
                return new ObjectResult(new ProblemDetails
                {
                    Title = "PLAN_LIMIT_EXCEEDED",
                    Detail = detail,
                    Status = StatusCodes.Status402PaymentRequired
                })
                {
                    StatusCode = StatusCodes.Status402PaymentRequired
                };
            }

            return BadRequest(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation(
            "Staff user created: {Email} with role {Role}",
            request.Email, request.Role);

        return CreatedAtAction(
            nameof(GetStaffUser),
            new { id = result.Data!.Id },
            result.Data);
    }

    /// <summary>
    /// Update staff user details. Owner cannot be demoted or deactivated.
    /// PUT /api/admin/staff/{id}
    /// Impersonation exclusion: requires real owner identity.
    /// </summary>
    [HttpPut("staff/{id:guid}")]
    [RejectImpersonation]
    [ProducesResponseType(typeof(StaffDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStaffUser(Guid id, [FromBody] UpdateStaffRequest request)
    {
        var result = await _adminService.UpdateStaffUserAsync(_tenantContext.TenantId, id, request);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to update staff user {UserId}: {Error}", id, result.Error);
            if (result.Error?.StartsWith("PLAN_LIMIT_EXCEEDED|", StringComparison.Ordinal) == true)
            {
                var detail = result.Error["PLAN_LIMIT_EXCEEDED|".Length..];
                return new ObjectResult(new ProblemDetails
                {
                    Title = "PLAN_LIMIT_EXCEEDED",
                    Detail = detail,
                    Status = StatusCodes.Status402PaymentRequired
                })
                {
                    StatusCode = StatusCodes.Status402PaymentRequired
                };
            }

            if (string.Equals(result.Error, "Staff user not found / المستخدم غير موجود", StringComparison.Ordinal))
                return NotFound(result.Error);

            if (result.Error?.StartsWith("OWNER_PROTECTED|", StringComparison.Ordinal) == true)
            {
                var detail = result.Error["OWNER_PROTECTED|".Length..];
                return new ObjectResult(new ProblemDetails
                {
                    Title = "OWNER_PROTECTED",
                    Detail = detail,
                    Status = StatusCodes.Status403Forbidden
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            return BadRequest(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation("Staff user updated: {UserId}", id);
        return Ok(result.Data);
    }

    /// <summary>
    /// Deactivate staff user (soft — marks inactive; not a hard delete).
    /// DELETE /api/admin/staff/{id}
    /// </summary>
    [HttpDelete("staff/{id:guid}")]
    [RejectImpersonation]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteStaffUser(Guid id)
    {
        var result = await _adminService.DeleteStaffUserAsync(_tenantContext.TenantId, id);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to delete staff user {UserId}: {Error}", id, result.Error);
            if (string.Equals(result.Error, "Staff user not found / المستخدم غير موجود", StringComparison.Ordinal))
                return NotFound(result.Error);
            if (result.Error?.StartsWith("OWNER_PROTECTED|", StringComparison.Ordinal) == true)
            {
                var detail = result.Error["OWNER_PROTECTED|".Length..];
                return new ObjectResult(new ProblemDetails
                {
                    Title = "OWNER_PROTECTED",
                    Detail = detail,
                    Status = StatusCodes.Status403Forbidden
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation("Staff user deactivated: {UserId}", id);
        return Ok(new { message = result.Message ?? "Staff account deactivated / تم إيقاف حساب الموظف" });
    }

    /// <summary>
    /// Reset staff user password.
    /// POST /api/admin/staff/{id}/reset-password
    /// Impersonation exclusion: requires real owner identity.
    /// </summary>
    [HttpPost("staff/{id:guid}/reset-password")]
    [RejectImpersonation]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResetStaffPassword(Guid id, [FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrEmpty(request.NewPassword))
            return BadRequest(new { error = "Password cannot be empty / كلمة المرور لا يمكن أن تكون فارغة" });

        var result = await _adminService.ResetStaffPasswordAsync(_tenantContext.TenantId, id, request.NewPassword);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to reset password for staff user {UserId}: {Error}", id, result.Error);
            if (string.Equals(result.Error, "Staff user not found / المستخدم غير موجود", StringComparison.Ordinal))
                return NotFound(result.Error);
            if (result.Error?.StartsWith("OWNER_PROTECTED|", StringComparison.Ordinal) == true)
            {
                var detail = result.Error["OWNER_PROTECTED|".Length..];
                return new ObjectResult(new ProblemDetails
                {
                    Title = "OWNER_PROTECTED",
                    Detail = detail,
                    Status = StatusCodes.Status403Forbidden
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation("Password reset for staff user: {UserId}", id);
        return Ok(new { message = result.Message ?? "Password reset successfully / تم إعادة تعيين كلمة المرور بنجاح" });
    }

    /// <summary>
    /// Activity for this staff account: lifecycle about them plus actions they performed.
    /// GET /api/admin/staff/{id}/activity
    /// </summary>
    [HttpGet("staff/{id:guid}/activity")]
    [ProducesResponseType(typeof(List<StaffActivityItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStaffActivity(Guid id)
    {
        var result = await _adminService.GetStaffActivityAsync(_tenantContext.TenantId, id);
        return result.IsSuccess ? Ok(result.Data) : NotFound(result.Error!);
    }

    /// <summary>
    /// Upload a staff profile photo. Persists on Identity + AppUser.
    /// POST /api/admin/staff/{id}/photo
    /// </summary>
    [HttpPost("staff/{id:guid}/photo")]
    [RejectImpersonation]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [ProducesResponseType(typeof(StaffDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadStaffPhoto(Guid id, IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No image uploaded / لم يتم رفع صورة" });
        if (file.Length > 2 * 1024 * 1024)
            return BadRequest(new { error = "Image must be ≤ 2MB / الصورة يجب ألا تتجاوز 2 ميجا" });

        var contentType = (file.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        var isAllowed = contentType is "image/jpeg" or "image/jpg" or "image/png" or "image/webp" or "image/gif";
        if (!isAllowed)
            return BadRequest(new { error = "Only JPEG/PNG/WebP/GIF images / صور JPEG أو PNG أو WebP أو GIF فقط" });

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            extension = contentType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".jpg"
            };
        }

        var safeName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await using var stream = file.OpenReadStream();
        var result = await _adminService.SetStaffPhotoAsync(
            _tenantContext.TenantId, id, stream, safeName, contentType);
        if (!result.IsSuccess)
            return result.Error == "Staff user not found / المستخدم غير موجود"
                ? NotFound(result.Error)
                : BadRequest(new { error = result.Error, message = result.Message });
        return Ok(result.Data);
    }

    /// <summary>
    /// Role → permission catalog for this gym. Effective grants include the tenant overlay.
    /// GET /api/admin/roles
    /// </summary>
    [HttpGet("roles")]
    [ProducesResponseType(typeof(RoleCatalogDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles([FromServices] IRolePermissionService roles)
    {
        var result = await roles.GetCatalogAsync(_tenantContext.TenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error!);
    }

    /// <summary>
    /// Replace the permission set for Manager, Receptionist, or Trainer in this gym.
    /// PUT /api/admin/roles/{role}
    /// </summary>
    [HttpPut("roles/{role}")]
    [RejectImpersonation]
    [ProducesResponseType(typeof(RoleAccessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateRole(
        string role,
        [FromBody] UpdateRolePermissionsRequest request,
        [FromServices] IRolePermissionService roles)
    {
        var result = await roles.UpdateRoleAsync(_tenantContext.TenantId, role, request);
        return RoleMutationResult(result);
    }

    /// <summary>
    /// Restore DefaultPermissionProvider for Manager, Receptionist, or Trainer.
    /// POST /api/admin/roles/{role}/reset
    /// </summary>
    [HttpPost("roles/{role}/reset")]
    [RejectImpersonation]
    [ProducesResponseType(typeof(RoleAccessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResetRole(string role, [FromServices] IRolePermissionService roles)
    {
        var result = await roles.ResetRoleAsync(_tenantContext.TenantId, role);
        return RoleMutationResult(result);
    }

    private IActionResult RoleMutationResult(GMS.Application.Common.Result<RoleAccessDto> result)
    {
        if (result.IsSuccess)
            return Ok(result.Data);
        if (result.Error?.StartsWith("ROLE_LOCKED|", StringComparison.Ordinal) == true)
        {
            var detail = result.Error["ROLE_LOCKED|".Length..];
            return new ObjectResult(new ProblemDetails
            {
                Title = "ROLE_LOCKED",
                Detail = detail,
                Status = StatusCodes.Status403Forbidden
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        return BadRequest(new { error = result.Error, message = result.Message });
    }
}
