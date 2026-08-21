namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>INVS-10 inventory reports. Feature-gated: inventory.</summary>
[Route("api/inventory/reports")]
[Authorize]
[FeatureFlag("inventory")]
public class InventoryReportsController : BaseApiController
{
    private readonly IInventoryReportService _reports;
    private readonly ITenantContext _tenantContext;

    public InventoryReportsController(IInventoryReportService reports, ITenantContext tenantContext)
    {
        _reports = reports;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Ops cards: OOS / low-stock / expiry windows. Valuation (qty×cost) only when caller
    /// has both inventory.view and reports.financial.view.
    /// </summary>
    [HttpGet("summary")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(InventorySummaryReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var includeValuation = User.HasClaim(Permissions.ClaimType, Permissions.ReportsFinancialView);
        var result = await _reports.GetSummaryAsync(_tenantContext.TenantId, includeValuation);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    /// <summary>Products at or below reorder min with suggested order qty.</summary>
    [HttpGet("reorder-suggestions")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<InventoryReorderSuggestionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReorderSuggestions()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var includeCost = User.HasClaim(Permissions.ClaimType, Permissions.InventoryManage)
            || User.HasClaim(Permissions.ClaimType, Permissions.InventoryPurchase);
        var result = await _reports.GetReorderSuggestionsAsync(_tenantContext.TenantId, includeCost);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    /// <summary>Tracked products with on-hand &gt; 0 and no sale movement in N days.</summary>
    [HttpGet("dead-stock")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<InventoryDeadStockRowDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeadStock([FromQuery] int daysIdle = 30)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var includeCost = User.HasClaim(Permissions.ClaimType, Permissions.ReportsFinancialView);
        var result = await _reports.GetDeadStockAsync(_tenantContext.TenantId, daysIdle, includeCost);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    /// <summary>Retail product sales performance for a UTC range (max 366 days).</summary>
    [HttpGet("product-performance")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<InventoryProductPerformanceRowDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ProductPerformance(
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        [FromQuery] int take = 50)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var includeMargin = User.HasClaim(Permissions.ClaimType, Permissions.ReportsFinancialView);
        var result = await _reports.GetProductPerformanceAsync(
            _tenantContext.TenantId, fromUtc, toUtc, includeMargin, take);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    /// <summary>Stock movement feed; max date range 366 days.</summary>
    [HttpGet("movements")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<InventoryMovementReportRowDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Movements([FromQuery] InventoryMovementQueryRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _reports.GetMovementsAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        if (!InventoryCostAccess.CanSeeCost(User))
            InventoryCostRedaction.RedactMovementReport(result.Data);
        return Ok(result.Data);
    }
}
