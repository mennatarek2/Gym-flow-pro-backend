namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>Platform commercial plan catalog — list prices, caps, features, sales availability.</summary>
[ApiController]
[Route("platform-api/plans")]
[Authorize(
    AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme,
    Policy = "PlatformSupportOrAbove")]
public class PlatformPlansController : ControllerBase
{
    private readonly ICommercialPlanService _plans;

    public PlatformPlansController(ICommercialPlanService plans)
    {
        _plans = plans;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CommercialPlanListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await _plans.ListAsync(ct);
        return Ok(items);
    }

    [HttpGet("{tier}")]
    [ProducesResponseType(typeof(CommercialPlanDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string tier, CancellationToken ct)
    {
        var plan = await _plans.GetAsync(tier, ct);
        if (plan == null)
            return NotFound(new { errorCode = "PLAN_NOT_FOUND", message = "Plan not found." });
        return Ok(plan);
    }

    [HttpGet("{tier}/history")]
    [ProducesResponseType(typeof(PlatformPagedResult<PlanChangeLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> History(
        string tier,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _plans.GetHistoryAsync(tier, page, pageSize, ct);
        return Ok(result);
    }

    [HttpPut("{tier}/metadata")]
    [Authorize(Policy = "PlatformAdminOnly")]
    [ProducesResponseType(typeof(CommercialPlanMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateMetadata(
        string tier,
        [FromBody] UpdatePlanMetadataRequest request,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _plans.UpdateMetadataAsync(tier, request, actor.Value, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPut("{tier}/pricing")]
    [Authorize(Policy = "PlatformAdminOnly")]
    [ProducesResponseType(typeof(CommercialPlanMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePricing(
        string tier,
        [FromBody] UpdatePlanPricingRequest request,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _plans.UpdatePricingAsync(tier, request, actor.Value, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPut("{tier}/caps")]
    [Authorize(Policy = "PlatformAdminOnly")]
    [ProducesResponseType(typeof(CommercialPlanMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateCaps(
        string tier,
        [FromBody] UpdatePlanCapsRequest request,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _plans.UpdateCapsAsync(tier, request, actor.Value, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPut("{tier}/features")]
    [Authorize(Policy = "PlatformAdminOnly")]
    [ProducesResponseType(typeof(CommercialPlanMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateFeatures(
        string tier,
        [FromBody] UpdatePlanFeaturesRequest request,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _plans.UpdateFeaturesAsync(tier, request, actor.Value, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("{tier}/sales-status")]
    [Authorize(Policy = "PlatformAdminOnly")]
    [ProducesResponseType(typeof(CommercialPlanMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetSalesStatus(
        string tier,
        [FromBody] UpdatePlanSalesStatusRequest request,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _plans.SetSalesStatusAsync(tier, request, actor.Value, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("{tier}/set-default")]
    [Authorize(Policy = "PlatformAdminOnly")]
    [ProducesResponseType(typeof(CommercialPlanMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetDefault(
        string tier,
        [FromBody] SetDefaultPlanRequest request,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _plans.SetDefaultAsync(tier, request, actor.Value, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    private Guid? RequireActorId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
