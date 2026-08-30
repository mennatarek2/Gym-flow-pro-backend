namespace GMS.Api.Platform.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>Cross-tenant usage rollup for the Platform Console overview — reads the same
/// platform.usage_counters table as the per-tenant Usage panel, just aggregated.</summary>
[ApiController]
[Route("platform-api/usage")]
[Authorize(
    AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme,
    Policy = "PlatformSupportOrAbove")]
public class PlatformUsageController : ControllerBase
{
    private readonly IPlatformUsageService _usage;

    public PlatformUsageController(IPlatformUsageService usage)
    {
        _usage = usage;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(PlatformUsageSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var result = await _usage.GetSummaryAsync(ct);
        return Ok(result);
    }
}
