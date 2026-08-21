namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Trials;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Free-trial issuance: staff-initiated, phone-OTP-verified two-step signup.
/// </summary>
[Route("api/trials")]
[Authorize]
[FeatureFlag("trials")]
public class TrialController : BaseApiController
{
    private readonly ITrialService _trialService;
    private readonly ITenantContext _tenantContext;

    public TrialController(ITrialService trialService, ITenantContext tenantContext)
    {
        _trialService = trialService;
        _tenantContext = tenantContext;
    }

    /// <summary>POST /api/trials/initiate</summary>
    [HttpPost("initiate")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(typeof(TrialInitiateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Initiate([FromBody] TrialInitiateRequest request)
    {
        var result = await _trialService.InitiateAsync(request, _tenantContext.TenantId);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>POST /api/trials/confirm</summary>
    [HttpPost("confirm")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(typeof(TrialConfirmResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Confirm([FromBody] TrialConfirmRequest request)
    {
        var result = await _trialService.ConfirmAsync(request, GetUserId(), _tenantContext.TenantId);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        if (result.Message?.StartsWith("PLAN_SOFT_CAP:", StringComparison.Ordinal) == true)
            Response.Headers["X-Plan-Soft-Cap"] = result.Message["PLAN_SOFT_CAP:".Length..];

        return Ok(result.Data);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    /// <summary>ITrialService encodes a machine-readable reason as a "CODE|message" prefix on failure.</summary>
    private IActionResult ProblemFromResult(string error)
    {
        var (code, message) = SplitReason(error);

        var statusCode = code switch
        {
            var c when c == TrialFailureReasons.TrialAlreadyUsed => StatusCodes.Status409Conflict,
            var c when c == TrialFailureReasons.PlanNotFound => StatusCodes.Status404NotFound,
            var c when c == TrialFailureReasons.PendingTrialNotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(detail: message, statusCode: statusCode, title: code);
    }

    private static (string Code, string Message) SplitReason(string error)
    {
        var separatorIndex = error.IndexOf('|');
        return separatorIndex < 0 ? ("ERROR", error) : (error[..separatorIndex], error[(separatorIndex + 1)..]);
    }
}
