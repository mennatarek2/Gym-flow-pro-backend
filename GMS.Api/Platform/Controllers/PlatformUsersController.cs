namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>Manages platform_support/platform_ops/platform_admin accounts. Entirely PlatformAdminOnly —
/// creating or reshaping who has platform-wide access is the most sensitive action in this console,
/// one tier stricter than subscription mutations.</summary>
[ApiController]
[Route("platform-api/platform-users")]
[Authorize(
    AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme,
    Policy = "PlatformAdminOnly")]
public class PlatformUsersController : ControllerBase
{
    private readonly IPlatformUserAdminService _users;

    public PlatformUsersController(IPlatformUserAdminService users)
    {
        _users = users;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformUserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await _users.ListAsync(ct);
        return Ok(items);
    }

    [HttpPost]
    [ProducesResponseType(typeof(PlatformUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreatePlatformUserRequest request, CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var (result, user) = await _users.CreateAsync(actor.Value, request, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return CreatedAtAction(nameof(List), user);
    }

    [HttpPost("{id:guid}/disable")]
    [ProducesResponseType(typeof(PlatformUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var (result, user) = await _users.DisableAsync(actor.Value, id, ClientIp(), ct);
        if (!result.Success)
            return result.ErrorCode == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
        return Ok(user);
    }

    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(typeof(PlatformUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var (result, user) = await _users.ReactivateAsync(actor.Value, id, ClientIp(), ct);
        if (!result.Success)
            return result.ErrorCode == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
        return Ok(user);
    }

    [HttpPut("{id:guid}/role")]
    [ProducesResponseType(typeof(PlatformUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangePlatformUserRoleRequest request, CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var (result, user) = await _users.ChangeRoleAsync(actor.Value, id, request.Role, ClientIp(), ct);
        if (!result.Success)
            return result.ErrorCode == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
        return Ok(user);
    }

    private Guid? RequireActorId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
