namespace GMS.Api.Controllers;

using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Debtors;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Front-desk debtors list: outstanding balances across partially_paid sales, plus a throttled
/// WhatsApp payment-reminder action.
/// </summary>
[Route("api/debtors")]
[Authorize]
[FeatureFlag("debtors")]
public class DebtorsController : BaseApiController
{
    private readonly IDebtorsService _debtorsService;
    private readonly ITenantContext _tenantContext;

    public DebtorsController(IDebtorsService debtorsService, ITenantContext tenantContext)
    {
        _debtorsService = debtorsService;
        _tenantContext = tenantContext;
    }

    /// <summary>GET /api/debtors?page=&amp;pageSize=&amp;format=csv</summary>
    [HttpGet]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDebtors(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? format = null,
        [FromQuery] Guid? memberId = null)
    {
        var tenantId = _tenantContext.TenantId;

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var allResult = await _debtorsService.GetAllDebtorsAsync(tenantId);
            if (!allResult.IsSuccess)
                return Problem(detail: allResult.Error, statusCode: StatusCodes.Status400BadRequest);

            var csv = BuildCsv(allResult.Data!);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "debtors.csv");
        }

        var result = await _debtorsService.GetDebtorsPagedAsync(tenantId, page, pageSize, memberId);
        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }

    /// <summary>GET /api/debtors/summary</summary>
    [HttpGet("summary")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary()
    {
        var result = await _debtorsService.GetSummaryAsync(_tenantContext.TenantId);
        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }

    /// <summary>GET /api/debtors/{memberId}/sales — outstanding sales for Collect Payment on Member 360.</summary>
    [HttpGet("{memberId:guid}/sales")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(typeof(MemberOutstandingSalesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOutstandingSales(Guid memberId)
    {
        var result = await _debtorsService.GetOutstandingSalesAsync(_tenantContext.TenantId, memberId);
        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }

    /// <summary>POST /api/debtors/{memberId}/remind</summary>
    [HttpPost("{memberId:guid}/remind")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Remind(Guid memberId)
    {
        var result = await _debtorsService.RemindAsync(memberId, _tenantContext.TenantId, GetUserId());
        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(new { message = "Reminder sent / تم إرسال التذكير" });
    }

    // ── Helpers ──

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
            var c when c == DebtorFailureReasons.ReminderThrottle => StatusCodes.Status429TooManyRequests,
            var c when c == DebtorFailureReasons.MemberNotFound => StatusCodes.Status404NotFound,
            var c when c == DebtorFailureReasons.NoOutstandingBalance => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(detail: message, statusCode: statusCode, title: code);
    }

    private static (string Code, string Message) SplitReason(string error)
    {
        var separatorIndex = error.IndexOf('|');
        return separatorIndex < 0 ? ("ERROR", error) : (error[..separatorIndex], error[(separatorIndex + 1)..]);
    }

    private static string BuildCsv(List<DebtorDto> debtors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MemberId,FullName,PhoneNumber,TotalDue,OldestDueDate,AgingBucket,LastPaymentAt");

        foreach (var d in debtors)
        {
            sb.Append(d.MemberId).Append(',')
              .Append(CsvEscape(d.FullName)).Append(',')
              .Append(CsvEscape(d.PhoneNumber)).Append(',')
              .Append(d.TotalDue.ToString("F2")).Append(',')
              .Append(d.OldestDueDate.ToString("yyyy-MM-dd")).Append(',')
              .Append(d.AgingBucket).Append(',')
              .Append(d.LastPaymentAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty)
              .AppendLine();
        }

        return sb.ToString();
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
