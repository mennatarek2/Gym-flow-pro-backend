namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

[ApiController]
[Route("platform-api/local-owner-recoveries")]
[Authorize(
    AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme,
    Policy = "PlatformCustomerAccess")]
public class PlatformLocalOwnerRecoveryController : ControllerBase
{
    private readonly ILocalOwnerRecoveryService _recoveries;

    public PlatformLocalOwnerRecoveryController(ILocalOwnerRecoveryService recoveries)
    {
        _recoveries = recoveries;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? customerId, [FromQuery] string? status, CancellationToken cancellationToken)
        => Ok(await _recoveries.ListAsync(customerId, status, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var includeCode = User.IsInRole("platform_ops") || User.IsInRole("platform_admin") || HasOpsPolicy();
        var row = await _recoveries.GetAsync(id, includeCode, cancellationToken);
        return row == null ? NotFound() : Ok(row);
    }

    [HttpPost("import-challenge")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformOpsOrAbove")]
    public async Task<IActionResult> Import([FromBody] ImportOwnerRecoveryChallengeRequest request, CancellationToken cancellationToken)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var row = await _recoveries.ImportChallengeAsync(request ?? new(), actor.Value, cancellationToken);
            return Ok(row);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformOpsOrAbove")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] OwnerRecoveryDecisionRequest request, CancellationToken cancellationToken)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            return Ok(await _recoveries.ApproveAsync(id, request ?? new(), actor.Value, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformOpsOrAbove")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] OwnerRecoveryDecisionRequest request, CancellationToken cancellationToken)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            return Ok(await _recoveries.RejectAsync(id, request ?? new(), actor.Value, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/revoke")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformOpsOrAbove")]
    public async Task<IActionResult> Revoke(Guid id, [FromBody] OwnerRecoveryDecisionRequest request, CancellationToken cancellationToken)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            return Ok(await _recoveries.RevokeAsync(id, request ?? new(), actor.Value, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    bool HasOpsPolicy()
    {
        var role = User.FindFirst("role")?.Value
            ?? User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst(PlatformAuthConstants.RoleClaimType)?.Value;
        return role is "platform_ops" or "platform_admin";
    }

    Guid? ActorId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
