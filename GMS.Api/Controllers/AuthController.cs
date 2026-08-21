namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using GMS.Application.DTOs.Auth;
using GMS.Application.Interfaces;

/// <summary>
/// Authentication endpoints for staff login, token refresh, and member OTP flow.
/// </summary>
[Route("api/auth")]
public class AuthController : BaseApiController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Authenticate a staff user with email + password.
    /// Returns JWT access token (15 min) and refresh token (30 days).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var ipAddress = GetIpAddress();
        var result = await _authService.LoginAsync(request, ipAddress);

        if (!result.IsSuccess)
        {
            if (result.Error?.StartsWith("SUBSCRIPTION_SUSPENDED|", StringComparison.Ordinal) == true)
            {
                var detail = result.Error["SUBSCRIPTION_SUSPENDED|".Length..];
                return new ObjectResult(new ProblemDetails
                {
                    Title = "SUBSCRIPTION_SUSPENDED",
                    Detail = detail,
                    Status = StatusCodes.Status402PaymentRequired
                })
                {
                    StatusCode = StatusCodes.Status402PaymentRequired
                };
            }

            return Unauthorized(new { error = result.Error });
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Refresh an expired access token using a valid refresh token.
    /// Implements sliding rotation: old token revoked, new pair issued.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var ipAddress = GetIpAddress();
        var result = await _authService.RefreshTokenAsync(request, ipAddress);

        if (!result.IsSuccess)
            return Unauthorized(new { error = result.Error });

        return Ok(result.Data);
    }

    /// <summary>
    /// Legacy Member App OTP send. Prefer POST /api/auth/member-activate.
    /// </summary>
    [HttpPost("member-otp")]
    [AllowAnonymous]
    [Obsolete("Member App Stage 0 uses POST /api/auth/member-activate (staff-issued activation code).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendMemberOtp([FromBody] MemberOtpRequest request)
    {
#pragma warning disable CS0618
        var result = await _authService.SendMemberOtpAsync(request);
#pragma warning restore CS0618

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(new { message = result.Message });
    }

    /// <summary>
    /// Legacy Member App OTP verify. Prefer POST /api/auth/member-activate.
    /// </summary>
    [HttpPost("member-verify")]
    [AllowAnonymous]
    [Obsolete("Member App Stage 0 uses POST /api/auth/member-activate (staff-issued activation code).")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyMemberOtp([FromBody] MemberOtpVerifyRequest request)
    {
        var ipAddress = GetIpAddress();
#pragma warning disable CS0618
        var result = await _authService.VerifyMemberOtpAsync(request, ipAddress);
#pragma warning restore CS0618

        if (!result.IsSuccess)
            return Unauthorized(new { error = result.Error });

        return Ok(result.Data);
    }

    /// <summary>
    /// Member App Stage 0 activation: Gym Code + staff one-time activation code → JWT.
    /// </summary>
    [HttpPost("member-activate")]
    [AllowAnonymous]
    [EnableRateLimiting("member-activate-policy")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ActivateMemberApp([FromBody] MemberActivateRequest request)
    {
        var ipAddress = GetIpAddress();
        var result = await _authService.ActivateMemberAppAsync(request, ipAddress);

        if (!result.IsSuccess)
            return Unauthorized(new { error = result.Error });

        return Ok(result.Data);
    }

    /// <summary>
    /// Extracts the client's IP address from the request.
    /// </summary>
    private string? GetIpAddress()
    {
        if (Request.Headers.ContainsKey("X-Forwarded-For"))
            return Request.Headers["X-Forwarded-For"].FirstOrDefault();

        return HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString();
    }
}
