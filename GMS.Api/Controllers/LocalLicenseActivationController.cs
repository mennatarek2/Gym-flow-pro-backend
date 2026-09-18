namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>
/// Customer-facing HyMotion Local activation/validation. Anonymous by necessity — a Local
/// installation has no platform staff credentials and, before activation, no credentials at all.
/// "Authentication" here is proving knowledge of a license key, which is why this is both
/// anonymous AND rate-limited (local-license-activate-policy) - see Program.cs.
///
/// This is the ONE place outside GMS.Platform's admin surface that touches licensing, and it is
/// deliberately thin: all anti-resale/lifecycle logic lives in LocalLicenseService, not here.
/// </summary>
[ApiController]
[Route("api/local-license")]
[AllowAnonymous]
public partial class LocalLicenseActivationController : BaseApiController
{
    private readonly ILocalLicenseService _licenses;
    private readonly IDeskFeedbackService _deskFeedback;

    public LocalLicenseActivationController(ILocalLicenseService licenses, IDeskFeedbackService deskFeedback)
    {
        _licenses = licenses;
        _deskFeedback = deskFeedback;
    }

    /// <summary>POST /api/local-license/activate — first-run (or re-run on the same machine).</summary>
    [HttpPost("activate")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> Activate([FromBody] ActivateLocalLicenseRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.InstallationId))
            return BadRequest(new { error = "licenseKey and installationId are required." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _licenses.ActivateAsync(request.LicenseKey, request.InstallationId, request.MachineFingerprint, ip, cancellationToken);

        if (!result.Success)
            return BadRequest(new { error = result.Result, message = result.Message });

        return Ok(result.License);
    }

    /// <summary>POST /api/local-license/check — does not consume a device slot.</summary>
    [HttpPost("check")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> Check([FromBody] ValidateLocalLicenseRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.InstallationId))
            return BadRequest(new { error = "licenseKey and installationId are required." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _licenses.CheckAsync(request.LicenseKey, request.InstallationId, ip, cancellationToken);

        if (!result.Success)
            return BadRequest(new { error = result.Result, message = result.Message });

        return Ok(new { success = true, result = result.Result, message = result.Message });
    }

    /// <summary>POST /api/local-license/release — drop this PC's slot after setup fails.</summary>
    [HttpPost("release")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> Release([FromBody] ValidateLocalLicenseRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.InstallationId))
            return BadRequest(new { error = "licenseKey and installationId are required." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _licenses.ReleaseAsync(request.LicenseKey, request.InstallationId, ip, cancellationToken);

        if (!result.Success)
            return BadRequest(new { error = result.Result, message = result.Message });

        return Ok(new { success = true, result = result.Result, message = result.Message });
    }

    /// <summary>POST /api/local-license/validate — periodic re-check from an already-activated
    /// installation. Local treats a failure here as "could not reach server", not an instant
    /// lock-out — see the Local client's grace-period handling (docs/local/LOCAL_LICENSING.md).</summary>
    [HttpPost("validate")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> Validate([FromBody] ValidateLocalLicenseRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.InstallationId))
            return BadRequest(new { error = "licenseKey and installationId are required." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _licenses.ValidateAsync(
            request.LicenseKey,
            request.InstallationId,
            ip,
            cancellationToken,
            request.GymCode,
            request.GymName,
            request.AppVersion);

        if (!result.Success)
            return BadRequest(new { error = result.Result, message = result.Message });

        return Ok(result.License);
    }

    /// <summary>POST /api/local-license/lifecycle-event — observational Local setup/restore events.
    /// Authenticated by license key + installation id. Idempotent.</summary>
    [HttpPost("lifecycle-event")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> LifecycleEvent([FromBody] RecordLocalLifecycleEventRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var recorded = await _licenses.RecordLifecycleEventAsync(request ?? new RecordLocalLifecycleEventRequest(), cancellationToken);
            return Ok(recorded);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_event", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "invalid_event", message = ex.Message });
        }
    }

    /// <summary>POST /api/local-license/desk-feedback — Local desk product feedback into Platform.
    /// Authenticated by license key + installation id (same model as lifecycle-event).</summary>
    [HttpPost("desk-feedback")]
    [EnableRateLimiting("local-license-activate-policy")]
    public async Task<IActionResult> DeskFeedback([FromBody] IngestLocalDeskFeedbackRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _deskFeedback.SubmitFromLocalLicenseAsync(
                request ?? new IngestLocalDeskFeedbackRequest(),
                cancellationToken);
            return Ok(created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_feedback", message = ex.Message });
        }
    }
}

public class ActivateLocalLicenseRequest
{
    public string LicenseKey { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public string? MachineFingerprint { get; set; }
}

public class ValidateLocalLicenseRequest
{
    public string LicenseKey { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public string? AppVersion { get; set; }
}
