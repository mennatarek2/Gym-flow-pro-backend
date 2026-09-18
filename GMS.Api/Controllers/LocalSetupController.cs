namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using GMS.Api.Filters;
using GMS.Application.DTOs.LocalSetup;
using GMS.Application.Interfaces;

/// <summary>
/// Local Edition setup. Anonymous so it can run with no login. 404 on SaaS.
/// Two modes: restore the existing gym's Owner, or replace the local gym after backup +
/// typed confirmation. License validation is required in both modes.
/// </summary>
[Route("api/local-setup")]
[AllowAnonymous]
[RequireLocalEdition]
public class LocalSetupController : BaseApiController
{
    private readonly ILocalFirstRunService _firstRun;

    public LocalSetupController(ILocalFirstRunService firstRun)
    {
        _firstRun = firstRun;
    }

    /// <summary>GET /api/local-setup/status</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(LocalFirstRunStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        return Ok(await _firstRun.GetStatusAsync(cancellationToken));
    }

    /// <summary>POST /api/local-setup/validate-license</summary>
    [HttpPost("validate-license")]
    [ProducesResponseType(typeof(LocalLicenseValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateLicense(
        [FromBody] LocalLicenseValidationRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _firstRun.ValidateLicenseAsync(request ?? new LocalLicenseValidationRequest(), cancellationToken));
    }

    /// <summary>POST /api/local-setup/prepare-backup</summary>
    [HttpPost("prepare-backup")]
    [ProducesResponseType(typeof(LocalBackupPrepareResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PrepareBackup(CancellationToken cancellationToken)
    {
        var result = await _firstRun.PrepareBackupAsync(cancellationToken);
        if (!result.Success)
            return BadRequest(new { error = result.ErrorCode ?? LocalSetupErrorCodes.BackupFailed, message = result.Message });

        return Ok(result);
    }

    /// <summary>POST /api/local-setup/complete</summary>
    [HttpPost("complete")]
    [ProducesResponseType(typeof(LocalFirstRunStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Complete([FromBody] LocalFirstRunRequest request, CancellationToken cancellationToken)
    {
        var result = await _firstRun.CompleteFirstRunAsync(request, ResolveActor(), cancellationToken);
        if (!result.IsSuccess)
        {
            var code = result.Message ?? LocalSetupErrorCodes.GymCreateFailed;
            var body = new
            {
                error = code,
                message = result.Error ?? "Setup failed.",
            };
            if (code == LocalSetupErrorCodes.OwnerRequired)
                return Unauthorized(body);
            return BadRequest(body);
        }

        return Ok(result.Data);
    }

    LocalSetupActor ResolveActor()
    {
        if (User.Identity?.IsAuthenticated != true)
            return LocalSetupActor.Anonymous;

        var isOwner = User.IsInRole("Owner");
        Guid? tenantId = null;
        var raw = User.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(raw, out var id))
            tenantId = id;
        return new LocalSetupActor(isOwner, tenantId);
    }
}
