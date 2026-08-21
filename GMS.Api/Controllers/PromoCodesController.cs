namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Promo;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Promo code management. All operations are automatically scoped to the current tenant.
/// </summary>
[Route("api/promo-codes")]
[Authorize]
public class PromoCodesController : BaseApiController
{
    private readonly IPromoService _promoService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PromoCodesController> _logger;

    public PromoCodesController(
        IPromoService promoService,
        ITenantContext tenantContext,
        ILogger<PromoCodesController> logger)
    {
        _promoService = promoService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>GET /api/promo-codes?activeOnly=&amp;validToday=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPromoCodes(
        [FromQuery] bool? activeOnly,
        [FromQuery] bool? validToday,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _promoService.GetPagedAsync(tenantId, activeOnly, validToday, page, pageSize);

        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }

    /// <summary>GET /api/promo-codes/{id}</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(typeof(PromoCodeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPromoCode(Guid id)
    {
        var result = await _promoService.GetByIdAsync(id);
        if (!result.IsSuccess)
            return NotFound(result.Error);

        return Ok(result.Data);
    }

    /// <summary>POST /api/promo-codes</summary>
    [HttpPost]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(PromoCodeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePromoCode([FromBody] CreatePromoCodeRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _promoService.CreateAsync(tenantId, request);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to create promo code: {Error}", result.Error);
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);
        }

        _logger.LogInformation("Promo code created: {PromoCodeId}", result.Data!.Id);

        return CreatedAtAction(nameof(GetPromoCode), new { id = result.Data!.Id }, result.Data);
    }

    /// <summary>PUT /api/promo-codes/{id}</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(PromoCodeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePromoCode(Guid id, [FromBody] UpdatePromoCodeRequest request)
    {
        var result = await _promoService.UpdateAsync(id, request);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to update promo code {PromoCodeId}: {Error}", id, result.Error);
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(result.Data);
    }

    /// <summary>DELETE /api/promo-codes/{id} — soft deactivate (IsActive = false), not a hard delete.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivatePromoCode(Guid id)
    {
        var result = await _promoService.DeactivateAsync(id);

        if (!result.IsSuccess)
            return NotFound(result.Error);

        return Ok(new { message = result.Message ?? "Promo code deactivated / تم إلغاء تفعيل كود الخصم" });
    }
}
