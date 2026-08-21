namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>Platform-admin subscription ops (self-serve tenant console lands in CP6/CP7).</summary>
[ApiController]
[Route("platform-api/tenants/{tenantId:guid}/subscription")]
[Authorize(
    AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme,
    Policy = "PlatformAdminOnly")]
public class PlatformSubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _subscriptions;

    public PlatformSubscriptionController(ISubscriptionService subscriptions)
    {
        _subscriptions = subscriptions;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SubscriptionStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        var status = await _subscriptions.GetStatusAsync(tenantId, ct);
        if (status == null)
            return NotFound(new { errorCode = "NO_LIVE_SUBSCRIPTION", message = "No live subscription." });
        return Ok(status);
    }

    [HttpPost("change-tier")]
    [ProducesResponseType(typeof(SubscriptionMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangeTier(Guid tenantId, [FromBody] ChangeTierRequest request, CancellationToken ct)
    {
        var result = await _subscriptions.ChangeTierAsync(
            tenantId,
            request.NewTier,
            request.EffectiveNow,
            SubscriptionInitiators.PlatformAdmin,
            GetPlatformAdminId(),
            request.Reason,
            ct);

        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("cancel")]
    [ProducesResponseType(typeof(SubscriptionMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Cancel(Guid tenantId, [FromBody] CancelSubscriptionRequest request, CancellationToken ct)
    {
        var result = await _subscriptions.CancelAsync(
            tenantId,
            request.Immediate,
            request.Reason,
            SubscriptionInitiators.PlatformAdmin,
            GetPlatformAdminId(),
            ct);

        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    private Guid? GetPlatformAdminId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
