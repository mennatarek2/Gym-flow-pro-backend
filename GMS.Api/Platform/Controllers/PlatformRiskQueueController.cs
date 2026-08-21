namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>CP7 churn early-warning risk queue for Platform Console.</summary>
[ApiController]
[Route("platform-api/risk-queue")]
[Authorize(
    AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme,
    Policy = "PlatformSupportOrAbove")]
public class PlatformRiskQueueController : ControllerBase
{
    private readonly IPlatformRiskQueueService _queue;

    public PlatformRiskQueueController(IPlatformRiskQueueService queue)
    {
        _queue = queue;
    }

    /// <summary>
    /// Sorted churn queue. Default bands: at_risk,critical. Pass band=healthy,watch,at_risk,critical to widen.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RiskQueueItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? band, CancellationToken ct)
    {
        var items = await _queue.ListAsync(band, ct);
        return Ok(items);
    }

    [HttpPost("{tenantId:guid}/assign")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Assign(
        Guid tenantId,
        [FromBody] AssignRiskQueueRequest request,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _queue.AssignAsync(
            tenantId, actor.Value, request.AssignedPlatformUserId, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("{tenantId:guid}/outcome")]
    [ProducesResponseType(typeof(RiskQueueOutcomeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecordOutcome(
        Guid tenantId,
        [FromBody] RecordRiskQueueOutcomeRequest request,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var (result, outcome) = await _queue.RecordOutcomeAsync(tenantId, actor.Value, request, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(outcome);
    }

    private Guid? RequireActorId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
