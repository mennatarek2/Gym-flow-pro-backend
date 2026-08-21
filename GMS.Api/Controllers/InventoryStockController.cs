namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>INVS-3 stock reads (no public set-quantity). Feature-gated: inventory.</summary>
[Route("api/inventory")]
[Authorize]
[FeatureFlag("inventory")]
public class InventoryStockController : BaseApiController
{
    private readonly IStockLedgerService _ledger;
    private readonly ITenantContext _tenantContext;

    public InventoryStockController(IStockLedgerService ledger, ITenantContext tenantContext)
    {
        _ledger = ledger;
        _tenantContext = tenantContext;
    }

    /// <summary>Browse on-hand for all tracked products (optional warehouse filter).</summary>
    [HttpGet("stock/board")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<StockBoardRowDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> StockBoard(
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] string? q = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _ledger.GetStockBoardAsync(_tenantContext.TenantId, warehouseId, q);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    /// <summary>On-hand for a product at a warehouse; optional recent movements.</summary>
    [HttpGet("stock")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(StockQueryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> QueryStock(
        [FromQuery] Guid productId,
        [FromQuery] Guid warehouseId,
        [FromQuery] bool includeMovements = false,
        [FromQuery] int movementTake = 50)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        if (productId == Guid.Empty || warehouseId == Guid.Empty)
            return BadRequest(new { error = "productId and warehouseId are required" });

        var result = await _ledger.QueryStockAsync(
            _tenantContext.TenantId, productId, warehouseId, includeMovements, movementTake);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        if (!InventoryCostAccess.CanSeeCost(User) && result.Data?.Movements != null)
            InventoryCostRedaction.RedactMovements(result.Data.Movements);
        return Ok(result.Data);
    }

    /// <summary>Per-warehouse on-hand breakdown for one product.</summary>
    [HttpGet("products/{id:guid}/stock")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(ProductStockBreakdownDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ProductStock(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _ledger.GetProductStockBreakdownAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }
}
