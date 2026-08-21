namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Promo;
using GMS.Application.DTOs.Sales;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Point-of-sale endpoints: promo validation/pricing and the atomic sale endpoint.
/// </summary>
[Route("api/sales")]
[Authorize]
[FeatureFlag("sales")]
public class SalesController : BaseApiController
{
    private const string IdempotencyKeyHeader = "X-Idempotency-Key";

    private readonly IPromoService _promoService;
    private readonly ISaleService _saleService;
    private readonly IInvoiceService _invoiceService;
    private readonly ITenantContext _tenantContext;

    public SalesController(
        IPromoService promoService,
        ISaleService saleService,
        IInvoiceService invoiceService,
        ITenantContext tenantContext)
    {
        _promoService = promoService;
        _saleService = saleService;
        _invoiceService = invoiceService;
        _tenantContext = tenantContext;
    }

    /// <summary>POST /api/sales/validate-promo — validates a promo code against a plan/member and returns the computed price.</summary>
    [HttpPost("validate-promo")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(typeof(PromoValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidatePromo([FromBody] ValidatePromoRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _promoService.ValidateAndPriceAsync(request.Code, request.PlanId, request.MemberId, tenantId);

        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }

    /// <summary>
    /// POST /api/sales — the atomic sale endpoint. The idempotency key may be supplied via the
    /// X-Idempotency-Key header or the request body's idempotencyKey field (header wins if both given).
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(typeof(SaleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateSale([FromBody] CreateSaleRequest request)
    {
        if (Request.Headers.TryGetValue(IdempotencyKeyHeader, out var headerKey) && !string.IsNullOrWhiteSpace(headerKey))
            request.IdempotencyKey = headerKey.ToString();

        var tenantId = _tenantContext.TenantId;
        var staffUserId = GetUserId();
        var callerPermissions = User.FindAll(Permissions.ClaimType).Select(c => c.Value).ToHashSet();

        var result = await _saleService.CreateSaleAsync(request, staffUserId, tenantId, callerPermissions);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        if (result.Data!.IsReplay)
            Response.Headers.Append("Idempotent-Replay", "true");

        return Ok(result.Data);
    }

    /// <summary>POST /api/sales/{id}/payments — records a payment against an outstanding balance.</summary>
    [HttpPost("{id:guid}/payments")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(typeof(SaleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecordPayment(Guid id, [FromBody] RecordPaymentRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var staffUserId = GetUserId();

        var result = await _saleService.RecordPaymentAsync(id, tenantId, staffUserId, request);

        if (!result.IsSuccess)
            return ProblemFromResult(result.Error!);

        return Ok(result.Data);
    }

    /// <summary>
    /// GET /api/sales/{id}/invoice — resolve the snapshotted invoice for a sale (desk print).
    /// Returns 404 <c>INVOICE_NOT_READY</c> while Hangfire is still creating the invoice.
    /// </summary>
    [HttpGet("{id:guid}/invoice")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoiceForSale(Guid id)
    {
        var idResult = await _invoiceService.GetOriginalInvoiceIdForSaleAsync(id);
        if (!idResult.IsSuccess)
        {
            return Problem(
                detail: idResult.Error ?? "Invoice not ready",
                statusCode: StatusCodes.Status404NotFound,
                title: "INVOICE_NOT_READY");
        }

        var invResult = await _invoiceService.GetByIdAsync(idResult.Data);
        if (!invResult.IsSuccess || invResult.Data is null)
        {
            return Problem(
                detail: invResult.Error ?? "Invoice not found",
                statusCode: StatusCodes.Status404NotFound,
                title: "INVOICE_NOT_READY");
        }

        return Ok(new
        {
            invoiceId = invResult.Data.Id,
            invoiceNumber = invResult.Data.InvoiceNumber,
            saleId = id,
            total = invResult.Data.Total,
            currency = invResult.Data.Currency
        });
    }

    // ── Helpers ──

    private Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// ISaleService encodes a machine-readable reason as a "CODE|message" prefix on failure.
    /// FORBIDDEN_DISCOUNT_OVERRIDE maps to 403, OPEN_SHIFT_REQUIRED to 409 (conflict with the
    /// current shift state), everything else is a 400.
    /// </summary>
    private IActionResult ProblemFromResult(string error)
    {
        var separatorIndex = error.IndexOf('|');
        var code = separatorIndex < 0 ? "ERROR" : error[..separatorIndex];
        var message = separatorIndex < 0 ? error : error[(separatorIndex + 1)..];

        var statusCode = code switch
        {
            var c when c == SaleFailureReasons.ForbiddenDiscountOverride => StatusCodes.Status403Forbidden,
            var c when c == SaleFailureReasons.OpenShiftRequired => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(detail: message, statusCode: statusCode, title: code);
    }
}
