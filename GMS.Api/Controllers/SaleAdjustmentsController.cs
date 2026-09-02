namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Sales;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

[Route("api/sales/adjustments")]
[Authorize]
public sealed class SaleAdjustmentsController : BaseApiController
{
    private readonly ISaleAdjustmentService _service;
    private readonly ITenantContext _tenantContext;

    public SaleAdjustmentsController(ISaleAdjustmentService service, ITenantContext tenantContext)
    {
        _service = service;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.ReportsFinancialView)]
    public async Task<IActionResult> List([FromQuery] Guid? saleId, CancellationToken ct)
    {
        var result = await _service.ListAsync(_tenantContext.TenantId, saleId, ct);
        return result.IsSuccess
            ? Ok(result.Data)
            : Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpPost]
    [HasPermission(Permissions.PaymentsRefundApprove)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSaleAdjustmentRequest request,
        CancellationToken ct)
    {
        var result = await _service.CreateAsync(_tenantContext.TenantId, GetUserId(), request, ct);
        return result.IsSuccess
            ? Ok(result.Data)
            : Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpPost("/api/sales/{saleId:guid}/reconcile-balance")]
    [HasPermission(Permissions.PaymentsRefundApprove)]
    public async Task<IActionResult> ReconcileBalance(Guid saleId, CancellationToken ct)
    {
        var result = await _service.ReconcileBalanceAsync(
            _tenantContext.TenantId, GetUserId(), saleId, ct);
        return result.IsSuccess
            ? Ok(result.Data)
            : Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
