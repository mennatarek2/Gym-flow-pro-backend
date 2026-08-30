namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Admin;
using GMS.Application.Interfaces;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;

/// <summary>
/// P2.2 — Platform Console management of a tenant's own staff accounts (Owner/Manager/Trainer/
/// Receptionist). Every mutation delegates to the existing tenant-side IAdminService (see
/// PlatformTenantStaffService) — this controller only adds platform authentication, the mandatory
/// audit reason, and platform audit visibility on top of the already-established staff domain.
/// Reads are PlatformSupportOrAbove (same tier as other tenant-detail reads); mutations are
/// PlatformOpsOrAbove, matching the existing tier for other operational-but-reversible tenant
/// actions (force-suspend/reactivate, extend-trial, feature overrides) rather than the stricter
/// PlatformAdminOnly reserved for billing-changing actions (change-tier/cancel) and platform-user
/// management. This specific policy assignment is a new decision for this feature — flagged in the
/// P2.2 report for confirmation rather than silently asserted.
/// </summary>
[ApiController]
[Route("platform-api/tenants/{tenantId:guid}/users")]
[Authorize(
    AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme,
    Policy = "PlatformSupportOrAbove")]
public class PlatformTenantUsersController : ControllerBase
{
    private const int MinReasonLength = 10;

    private readonly IPlatformTenantStaffService _staff;

    public PlatformTenantUsersController(IPlatformTenantStaffService staff)
    {
        _staff = staff;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<StaffListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid tenantId, CancellationToken ct)
    {
        var result = await _staff.ListAsync(tenantId, ct);
        if (!result.IsSuccess)
            return BadRequest(new { errorMessage = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(typeof(StaffDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status402PaymentRequired)]
    public async Task<IActionResult> Create(Guid tenantId, [FromBody] CreateStaffRequest request, CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _staff.CreateAsync(tenantId, actor.Value, request, ClientIp(), ct);
        if (!result.Success)
            return MapError(result);

        return CreatedAtAction(nameof(List), new { tenantId }, result.Staff);
    }

    [HttpPost("{staffId:guid}/disable")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(typeof(StaffDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Disable(
        Guid tenantId, Guid staffId, [FromBody] DisableTenantStaffRequest request, CancellationToken ct)
    {
        var reasonError = ValidateReason(request.Reason);
        if (reasonError != null)
            return BadRequest(reasonError);

        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _staff.DisableAsync(tenantId, staffId, actor.Value, request.Reason.Trim(), ClientIp(), ct);
        return result.Success ? Ok(result.Staff) : MapError(result);
    }

    [HttpPost("{staffId:guid}/reactivate")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(typeof(StaffDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reactivate(
        Guid tenantId, Guid staffId, [FromBody] ReactivateTenantStaffRequest request, CancellationToken ct)
    {
        var reasonError = ValidateReason(request.Reason);
        if (reasonError != null)
            return BadRequest(reasonError);

        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _staff.ReactivateAsync(tenantId, staffId, actor.Value, request.Reason.Trim(), ClientIp(), ct);
        return result.Success ? Ok(result.Staff) : MapError(result);
    }

    [HttpPut("{staffId:guid}/role")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(typeof(StaffDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeRole(
        Guid tenantId, Guid staffId, [FromBody] ChangeTenantStaffRoleRequest request, CancellationToken ct)
    {
        var reasonError = ValidateReason(request.Reason);
        if (reasonError != null)
            return BadRequest(reasonError);
        if (string.IsNullOrWhiteSpace(request.Role))
            return BadRequest(new { errorCode = "ROLE_REQUIRED", errorMessage = "role is required." });

        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _staff.ChangeRoleAsync(tenantId, staffId, request.Role.Trim(), actor.Value, request.Reason.Trim(), ClientIp(), ct);
        return result.Success ? Ok(result.Staff) : MapError(result);
    }

    private static object? ValidateReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < MinReasonLength
            ? new { errorCode = "REASON_REQUIRED", errorMessage = $"reason is required (min {MinReasonLength} characters) for the audit log." }
            : null;

    private IActionResult MapError(GMS.Application.Interfaces.PlatformStaffMutationResult result)
    {
        var body = new { errorCode = result.ErrorCode, errorMessage = result.ErrorMessage };
        return result.ErrorCode switch
        {
            "NOT_FOUND" => NotFound(body),
            "OWNER_PROTECTED" => new ObjectResult(body) { StatusCode = StatusCodes.Status403Forbidden },
            "PLAN_LIMIT_EXCEEDED" => new ObjectResult(body) { StatusCode = StatusCodes.Status402PaymentRequired },
            _ => BadRequest(body)
        };
    }

    private Guid? RequireActorId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
