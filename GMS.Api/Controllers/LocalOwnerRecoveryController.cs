namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using GMS.Api.Filters;
using GMS.Application.DTOs.Auth;
using GMS.Application.Interfaces;

[Route("api/auth/owner-recovery")]
[AllowAnonymous]
[RequireLocalEdition]
public class LocalOwnerRecoveryController : BaseApiController
{
    private readonly ILocalOwnerRecoveryService _recovery;

    public LocalOwnerRecoveryController(ILocalOwnerRecoveryService recovery)
    {
        _recovery = recovery;
    }

    [HttpGet("context")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> Context(CancellationToken cancellationToken) =>
        Ok(await _recovery.GetContextAsync(cancellationToken));

    [HttpGet("status")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken) =>
        Ok(await _recovery.GetStatusAsync(cancellationToken));

    [HttpPost("request")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> RequestRecovery(CancellationToken cancellationToken)
    {
        var result = await _recovery.StartAsync(cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error ?? result.Message });
        return Ok(result.Data);
    }

    [HttpPost("submit-code")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> SubmitCode([FromBody] SubmitOwnerRecoveryCodeRequest? request, CancellationToken cancellationToken)
    {
        var result = await _recovery.SubmitCodeAsync(request?.RecoveryCode ?? string.Empty, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { error = "invalid_recovery", message = result.Error ?? result.Message });
        return Ok(result.Data);
    }

    [HttpPost("complete")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> Complete([FromBody] CompleteOwnerRecoveryRequest? request, CancellationToken cancellationToken)
    {
        var result = await _recovery.CompleteAsync(request ?? new CompleteOwnerRecoveryRequest(), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { error = "invalid_recovery", message = result.Error ?? result.Message });
        return Ok(result.Data);
    }

    [HttpPost("cancel")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> Cancel(CancellationToken cancellationToken) =>
        Ok(await _recovery.CancelAsync(cancellationToken));
}
