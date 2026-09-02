namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Refunds;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Two-step refund flow: request, then approve (executes immediately) or reject.
/// </summary>
[Route("api/refunds")]
[Authorize]
[FeatureFlag("refunds")]
public class RefundsController : BaseApiController
{
    private readonly IRefundService _refundService;
    private readonly ITenantContext _tenantContext;

    public RefundsController(IRefundService refundService, ITenantContext tenantContext)
    {
        _refundService = refundService;
        _tenantContext = tenantContext;
    }

    /// <summary>POST /api/refunds</summary>
    [HttpPost]
    [HasPermission(Permissions.PaymentsRefundRequest)]
    [ProducesResponseType(typeof(RefundDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestRefund([FromBody] RequestRefundRequest request)
    {
        var result = await _refundService.RequestAsync(
            request.SaleId, request.Amount, request.Method, request.Reason, GetUserId(), _tenantContext.TenantId,
            request.PaymentTransactionId);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>POST /api/refunds/{id}/approve</summary>
    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.PaymentsRefundApprove)]
    [ProducesResponseType(typeof(RefundDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var result = await _refundService.ApproveAsync(id, GetUserId(), _tenantContext.TenantId);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>POST /api/refunds/{id}/reject</summary>
    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.PaymentsRefundApprove)]
    [ProducesResponseType(typeof(RefundDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectRefundRequest request)
    {
        var result = await _refundService.RejectAsync(id, request.Note, GetUserId(), _tenantContext.TenantId);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>GET /api/refunds?saleId=&amp;memberId=&amp;status=</summary>
    [HttpGet]
    [HasPermission(Permissions.PaymentsRefundApprove)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRefunds(
        [FromQuery] Guid? saleId, [FromQuery] Guid? memberId, [FromQuery] string? status)
    {
        var result = await _refundService.GetListAsync(_tenantContext.TenantId, saleId, memberId, status);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    /// <summary>IRefundService encodes a machine-readable reason as a "CODE|message" prefix on failure.</summary>
    private IActionResult ProblemFromResult(string error)
    {
        var (code, message) = SplitReason(error);

        var statusCode = code switch
        {
            var c when c == RefundFailureReasons.SaleNotFound => StatusCodes.Status404NotFound,
            var c when c == RefundFailureReasons.RefundNotFound => StatusCodes.Status404NotFound,
            var c when c == RefundFailureReasons.SaleFullyRefunded => StatusCodes.Status409Conflict,
            var c when c == RefundFailureReasons.NotAwaitingApproval => StatusCodes.Status409Conflict,
            var c when c == RefundFailureReasons.OpenShiftRequired => StatusCodes.Status409Conflict,
            var c when c == RefundFailureReasons.SelfApprovalForbidden => StatusCodes.Status403Forbidden,
            var c when c == RefundFailureReasons.GatewayRefundUnsupported => StatusCodes.Status409Conflict,
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
