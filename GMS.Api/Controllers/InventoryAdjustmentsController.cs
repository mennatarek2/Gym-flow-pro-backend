namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>INVS-4 opening stock &amp; adjustments.</summary>
[Route("api/inventory/adjustments")]
[Authorize]
[FeatureFlag("inventory")]
public class InventoryAdjustmentsController : BaseApiController
{
    private readonly IStockAdjustmentService _adjustments;
    private readonly ITenantContext _tenantContext;

    public InventoryAdjustmentsController(
        IStockAdjustmentService adjustments,
        ITenantContext tenantContext)
    {
        _adjustments = adjustments;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(List<StockAdjustmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status = null, [FromQuery] int take = 50)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _adjustments.ListAsync(_tenantContext.TenantId, status, take);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockAdjustmentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateStockAdjustmentRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _adjustments.CreateDraftAsync(_tenantContext.TenantId, staffId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockAdjustmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _adjustments.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/post")]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockAdjustmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Post(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _adjustments.PostAsync(_tenantContext.TenantId, staffId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockAdjustmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _adjustments.CancelAsync(_tenantContext.TenantId, staffId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
