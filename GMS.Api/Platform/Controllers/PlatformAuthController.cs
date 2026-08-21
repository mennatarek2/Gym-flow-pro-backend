namespace GMS.Api.Platform.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>Platform-plane auth — never shares policies with tenant controllers.</summary>
[ApiController]
[Route("platform-api/auth")]
public class PlatformAuthController : ControllerBase
{
    private readonly IPlatformAuthService _auth;

    public PlatformAuthController(IPlatformAuthService auth)
    {
        _auth = auth;
    }

    /// <summary>
    /// Login. Without MFA configured → MFA_SETUP_REQUIRED (forced setup, no access token).
    /// With MFA → requires mfaCode.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PlatformLoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] PlatformLoginRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _auth.LoginAsync(request, ip);

        if (result.MfaSetupRequired)
            return StatusCode(StatusCodes.Status403Forbidden, result);

        if (!result.Success)
            return Unauthorized(result);

        return Ok(result);
    }

    /// <summary>Completes forced MFA enrollment using the setup token from login.</summary>
    [HttpPost("mfa/setup")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PlatformLoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompleteMfaSetup([FromBody] PlatformMfaSetupRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _auth.CompleteMfaSetupAsync(request, ip);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }
}

/// <summary>
/// Authenticated ping used by isolation tests.
/// Requires PlatformBearer — tenant JWTs must hard-fail (401).
/// </summary>
[ApiController]
[Route("platform-api")]
[Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme)]
public class PlatformPingController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        var sub = User.FindFirst("sub")?.Value;
        var role = User.FindFirst(PlatformAuthConstants.RoleClaimType)?.Value
                   ?? User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        return Ok(new
        {
            ok = true,
            plane = "platform",
            sub,
            role,
            audience = PlatformAuthConstants.Audience
        });
    }
}
