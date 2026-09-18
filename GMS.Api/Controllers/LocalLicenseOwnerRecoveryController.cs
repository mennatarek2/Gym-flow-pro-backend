namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

public partial class LocalLicenseActivationController
{
    /// <summary>POST /api/local-license/owner-recovery/request — gym PC creates or resumes a
    /// recovery request. Authenticated by license key + installation id, not a staff JWT.</summary>
    [HttpPost("owner-recovery/request")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> OwnerRecoveryRequest(
        [FromBody] LocalOwnerRecoveryAuthRequest request,
        [FromServices] ILocalOwnerRecoveryService recoveries,
        CancellationToken cancellationToken)
    {
        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            return Ok(await recoveries.RequestFromInstallationAsync(request ?? new(), ip, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_recovery", message = ex.Message });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "invalid_recovery", message = "This recovery request could not be submitted." });
        }
    }

    [HttpPost("owner-recovery/poll")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> OwnerRecoveryPoll(
        [FromBody] LocalOwnerRecoveryAuthRequest request,
        [FromServices] ILocalOwnerRecoveryService recoveries,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await recoveries.PollFromInstallationAsync(request ?? new(), cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_recovery", message = ex.Message });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "invalid_recovery", message = "This recovery request could not be checked." });
        }
    }

    [HttpPost("owner-recovery/complete")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> OwnerRecoveryComplete(
        [FromBody] LocalOwnerRecoveryAuthRequest request,
        [FromServices] ILocalOwnerRecoveryService recoveries,
        CancellationToken cancellationToken)
    {
        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            return Ok(await recoveries.CompleteFromInstallationAsync(request ?? new(), ip, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_recovery", message = ex.Message });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "invalid_recovery", message = "This recovery request could not be completed." });
        }
    }
}
