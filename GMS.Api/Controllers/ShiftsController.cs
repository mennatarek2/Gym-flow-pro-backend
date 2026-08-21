namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Shifts;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Cash-drawer shift endpoints: open/close with blind-count reconciliation, cash movements, and
/// variance approval.
/// </summary>
[Route("api/shifts")]
[Authorize]
[FeatureFlag("shifts")]
public class ShiftsController : BaseApiController
{
    private readonly IShiftService _shiftService;
    private readonly ITenantContext _tenantContext;

    public ShiftsController(IShiftService shiftService, ITenantContext tenantContext)
    {
        _shiftService = shiftService;
        _tenantContext = tenantContext;
    }

    /// <summary>POST /api/shifts/open</summary>
    [HttpPost("open")]
    [HasPermission(Permissions.ShiftOpen)]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Open([FromBody] OpenShiftRequest request)
    {
        var result = await _shiftService.OpenAsync(request.OpeningFloat, GetUserId(), _tenantContext.TenantId);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>GET /api/shifts/current — returns null if no shift is open. ExpectedCash is
    /// always null here (blind count) since it's only computed at close time.</summary>
    [HttpGet("current")]
    [HasPermission(Permissions.ShiftOpen)]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrent()
    {
        var result = await _shiftService.GetCurrentAsync(GetUserId(), _tenantContext.TenantId);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>POST /api/shifts/current/close</summary>
    [HttpPost("current/close")]
    [HasPermission(Permissions.ShiftClose)]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CloseCurrent([FromBody] CloseShiftRequest request)
    {
        var result = await _shiftService.CloseAsync(
            request.CountedCash, request.VarianceNote, GetUserId(), _tenantContext.TenantId);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>
    /// POST /api/shifts/current/movements — paid_in always allowed; paid_out above the tenant's
    /// approval threshold requires ShiftReconcileApprove.
    /// </summary>
    [HttpPost("current/movements")]
    [HasPermission(Permissions.ShiftOpen)]
    [ProducesResponseType(typeof(CashMovementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordMovement([FromBody] RecordMovementRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var staffUserId = GetUserId();

        var shiftId = await _shiftService.GetCurrentOpenShiftIdAsync(staffUserId, tenantId);
        if (shiftId == null)
            return Problem(
                detail: "No open shift / لا توجد وردية مفتوحة",
                statusCode: StatusCodes.Status409Conflict,
                title: ShiftFailureReasons.NoOpenShift);

        var callerPermissions = User.FindAll(Permissions.ClaimType).Select(c => c.Value).ToHashSet();

        var result = await _shiftService.RecordMovementAsync(
            shiftId.Value, request.Type, request.Amount, request.ReferenceId, request.Reason,
            staffUserId, tenantId, callerPermissions);

        if (!result.IsSuccess)
            return ProblemFromMovementResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>POST /api/shifts/{id}/approve</summary>
    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.ShiftReconcileApprove)]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveShiftRequest request)
    {
        var result = await _shiftService.ApproveAsync(id, request.Note, GetUserId(), _tenantContext.TenantId);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>POST /api/shifts/{id}/force-close</summary>
    [HttpPost("{id:guid}/force-close")]
    [Authorize(Policy = "ManagerOrAbove")]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ForceClose(Guid id)
    {
        var result = await _shiftService.ForceCloseAsync(id, GetUserId(), _tenantContext.TenantId);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>GET /api/shifts?from=&amp;to=&amp;userId=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    [Authorize(Policy = "ManagerOrAbove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetShifts(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _shiftService.GetPagedAsync(_tenantContext.TenantId, from, to, userId, page, pageSize);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>GET /api/shifts/open-summary → { openShifts, totalCashInDrawers }</summary>
    [HttpGet("open-summary")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(ShiftOpenSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOpenSummary()
    {
        var result = await _shiftService.GetOpenSummaryAsync(_tenantContext.TenantId);

        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }

    // ── Helpers ──

    private Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    /// <summary>IShiftService encodes a machine-readable reason as a "CODE|message" prefix on failure.</summary>
    private IActionResult ProblemFromResult(string error)
    {
        var (code, message) = SplitReason(error);

        var statusCode = code switch
        {
            var c when c == ShiftFailureReasons.ShiftAlreadyOpen => StatusCodes.Status409Conflict,
            var c when c == ShiftFailureReasons.NoOpenShift => StatusCodes.Status409Conflict,
            var c when c == ShiftFailureReasons.NotAwaitingApproval => StatusCodes.Status409Conflict,
            var c when c == ShiftFailureReasons.ShiftNotOpen => StatusCodes.Status409Conflict,
            var c when c == ShiftFailureReasons.ShiftNotFound => StatusCodes.Status404NotFound,
            var c when c == ShiftFailureReasons.ManagerApprovalRequired => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(detail: message, statusCode: statusCode, title: code);
    }

    private IActionResult ProblemFromMovementResult(string error)
    {
        var (code, message) = SplitReason(error);

        var statusCode = code switch
        {
            var c when c == ShiftFailureReasons.ManagerApprovalRequired => StatusCodes.Status403Forbidden,
            var c when c == ShiftFailureReasons.NoOpenShift => StatusCodes.Status409Conflict,
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
