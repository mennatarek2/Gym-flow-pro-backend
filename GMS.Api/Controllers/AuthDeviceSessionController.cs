namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Filters;
using GMS.Application.DTOs.Auth;
using GMS.Application.Interfaces;
using GMS.Infrastructure.Configuration;

/// <summary>
/// Local Edition keep-signed-in store. Anonymous because the login page restores a session
/// before the user has an access token. 404 on SaaS. Gym identity is loaded from live setup
/// status and is never written or cleared here.
/// </summary>
[Route("api/auth")]
[AllowAnonymous]
[RequireLocalEdition]
public class AuthDeviceSessionController : BaseApiController
{
    private readonly LocalDeviceSessionStore _deviceSession;
    private readonly LocalLicenseStore _licenseStore;
    private readonly ILocalFirstRunService _firstRun;
    private readonly ILogger<AuthDeviceSessionController> _logger;

    public AuthDeviceSessionController(
        LocalDeviceSessionStore deviceSession,
        LocalLicenseStore licenseStore,
        ILocalFirstRunService firstRun,
        ILogger<AuthDeviceSessionController> logger)
    {
        _deviceSession = deviceSession;
        _licenseStore = licenseStore;
        _firstRun = firstRun;
        _logger = logger;
    }

    /// <summary>GET /api/auth/device-session</summary>
    [HttpGet("device-session")]
    [ProducesResponseType(typeof(DeviceSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var live = await _firstRun.GetStatusAsync(cancellationToken);
        var session = _deviceSession.LoadIfBound(live.GymCode, _licenseStore.TryGetInstallationId());
        if (session == null)
            return NoContent();

        return Ok(new DeviceSessionResponse { RefreshToken = session.RefreshToken });
    }

    /// <summary>POST /api/auth/device-session</summary>
    [HttpPost("device-session")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Save([FromBody] SaveDeviceSessionRequest? request, CancellationToken cancellationToken)
    {
        var refresh = request?.RefreshToken?.Trim();
        if (string.IsNullOrWhiteSpace(refresh))
            return BadRequest(new { error = "Refresh token is required." });

        var live = await _firstRun.GetStatusAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(live.GymCode))
            return BadRequest(new { error = "This PC is not bound to a gym." });

        var installationId = _licenseStore.TryGetInstallationId() ?? _licenseStore.GetOrCreateInstallationId();
        _deviceSession.Save(new LocalDeviceSessionFileContent
        {
            RefreshToken = refresh,
            GymCode = live.GymCode,
            InstallationId = installationId,
            TenantId = live.TenantId,
            SavedAtUtc = DateTime.UtcNow,
        });
        return NoContent();
    }

    /// <summary>DELETE /api/auth/device-session — sign out / switch account. Does not clear gym identity.</summary>
    [HttpDelete("device-session")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Delete()
    {
        _deviceSession.Clear();
        _logger.LogInformation("Local device session cleared.");
        return NoContent();
    }
}
