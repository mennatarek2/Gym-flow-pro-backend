namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.DTOs.CallSheet;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Daily member follow-up queue. Membership / sales / attendance stay owned by those modules.
/// Not feature-flagged.
/// </summary>
[Route("api/call-sheet")]
[Authorize]
public class CallSheetController : BaseApiController
{
    private readonly ICallSheetService _callSheetService;
    private readonly ITenantContext _tenantContext;

    public CallSheetController(ICallSheetService callSheetService, ITenantContext tenantContext)
    {
        _callSheetService = callSheetService;
        _tenantContext = tenantContext;
    }

    /// <summary>GET /api/call-sheet?date=today&amp;reason=&amp;priority=&amp;status=&amp;assignee=&amp;q=</summary>
    [HttpGet]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueue(
        [FromQuery] string? date,
        [FromQuery] string? reason,
        [FromQuery] string? priority,
        [FromQuery] string? status,
        [FromQuery] string? assignee,
        [FromQuery] string? q)
    {
        var result = await _callSheetService.GetQueueAsync(
            _tenantContext.TenantId, GetUserId(), date, reason, priority, status, assignee, q);
        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }

    /// <summary>GET /api/call-sheet/summary</summary>
    [HttpGet("summary")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary()
    {
        var result = await _callSheetService.GetSummaryAsync(_tenantContext.TenantId);
        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }

    /// <summary>GET /api/call-sheet/expiring?days=7 — legacy dashboard list.</summary>
    [HttpGet("expiring")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExpiring([FromQuery] int days = 7)
    {
        var result = await _callSheetService.GetExpiringAsync(_tenantContext.TenantId, days);
        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }

    /// <summary>GET /api/call-sheet/renewal-rate?from=&amp;to=&amp;staffUserId=</summary>
    [HttpGet("renewal-rate")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRenewalRate(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? staffUserId)
    {
        var result = await _callSheetService.GetRenewalRateAsync(_tenantContext.TenantId, from, to, staffUserId);
        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }

    /// <summary>GET /api/call-sheet/{id}</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _callSheetService.GetByIdAsync(id, _tenantContext.TenantId);
        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>POST /api/call-sheet</summary>
    [HttpPost]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateFollowUpRequest request)
    {
        var result = await _callSheetService.CreateAsync(_tenantContext.TenantId, GetUserId(), request);
        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>POST /api/call-sheet/{id}/outcome — id is follow-up id.</summary>
    [HttpPost("{id:guid}/outcome")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordOutcome(Guid id, [FromBody] RecordCallOutcomeRequest request)
    {
        var result = await _callSheetService.RecordOutcomeAsync(id, _tenantContext.TenantId, GetUserId(), request);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(new { message = "Outcome recorded / تم تسجيل النتيجة" });
    }

    /// <summary>POST /api/call-sheet/{id}/complete</summary>
    [HttpPost("{id:guid}/complete")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteFollowUpRequest? request)
    {
        var result = await _callSheetService.CompleteAsync(
            id, _tenantContext.TenantId, GetUserId(), request?.Note);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(new { message = "Follow-up completed / تم إكمال المتابعة" });
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private IActionResult ProblemFromResult(string error)
    {
        var (code, message) = SplitReason(error);

        var statusCode = code switch
        {
            var c when c == CallSheetFailureReasons.MembershipNotFound => StatusCodes.Status404NotFound,
            var c when c == CallSheetFailureReasons.StaffUserNotFound => StatusCodes.Status404NotFound,
            var c when c == CallSheetFailureReasons.FollowUpNotFound => StatusCodes.Status404NotFound,
            var c when c == CallSheetFailureReasons.MemberNotFound => StatusCodes.Status404NotFound,
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
