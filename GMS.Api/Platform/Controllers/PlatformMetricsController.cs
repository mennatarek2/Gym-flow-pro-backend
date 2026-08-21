namespace GMS.Api.Platform.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>CP8 SaaS metrics — MRR, movement, churn, conversion, tier mix.</summary>
[ApiController]
[Route("platform-api/metrics")]
[Authorize(
    AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme,
    Policy = "PlatformSupportOrAbove")]
public class PlatformMetricsController : ControllerBase
{
    private readonly IPlatformMetricsService _metrics;

    public PlatformMetricsController(IPlatformMetricsService metrics)
    {
        _metrics = metrics;
    }

    /// <summary>Current (or as-of) MRR/ARR. Annual prices ÷12. Query: asOf=yyyy-MM-dd (Cairo calendar day).</summary>
    [HttpGet("mrr")]
    [ProducesResponseType(typeof(MrrSnapshotDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMrr([FromQuery] DateOnly? asOf, CancellationToken ct)
    {
        var result = await _metrics.GetMrrAsync(asOf, ct);
        return Ok(result);
    }

    /// <summary>New / expansion / contraction / churned MRR for [from, to] (inclusive Cairo days).</summary>
    [HttpGet("movement")]
    [ProducesResponseType(typeof(MrrMovementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMovement(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        if (from is null || to is null)
            return BadRequest(new { errorCode = "RANGE_REQUIRED", errorMessage = "from and to (yyyy-MM-dd) are required." });

        var result = await _metrics.GetMovementAsync(from.Value, to.Value, ct);
        return Ok(result);
    }

    /// <summary>Gross churn rate + signup-month cohort retention as of period end.</summary>
    [HttpGet("churn")]
    [ProducesResponseType(typeof(ChurnMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetChurn(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        if (from is null || to is null)
            return BadRequest(new { errorCode = "RANGE_REQUIRED", errorMessage = "from and to (yyyy-MM-dd) are required." });

        var result = await _metrics.GetChurnAsync(from.Value, to.Value, ct);
        return Ok(result);
    }

    /// <summary>Trial → paid conversion for trials started in [from, to].</summary>
    [HttpGet("conversion")]
    [ProducesResponseType(typeof(ConversionMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetConversion(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        if (from is null || to is null)
            return BadRequest(new { errorCode = "RANGE_REQUIRED", errorMessage = "from and to (yyyy-MM-dd) are required." });

        var result = await _metrics.GetConversionAsync(from.Value, to.Value, ct);
        return Ok(result);
    }

    /// <summary>Active paying tenant count and MRR by plan_tier.</summary>
    [HttpGet("tier-distribution")]
    [ProducesResponseType(typeof(TierDistributionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTierDistribution([FromQuery] DateOnly? asOf, CancellationToken ct)
    {
        var result = await _metrics.GetTierDistributionAsync(asOf, ct);
        return Ok(result);
    }
}
