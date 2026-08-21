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

/// <summary>INVS-9 stock counts.</summary>
[Route("api/inventory/counts")]
[Authorize]
[FeatureFlag("stock_management")]
public class InventoryCountsController : BaseApiController
{
    private readonly IStockCountService _counts;
    private readonly ITenantContext _tenantContext;

    public InventoryCountsController(IStockCountService counts, ITenantContext tenantContext)
    {
        _counts = counts;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(List<StockCountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _counts.ListAsync(_tenantContext.TenantId, status);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockCountDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _counts.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockCountDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateStockCountRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _counts.CreateAsync(_tenantContext.TenantId, staffId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:guid}/lines")]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockCountDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLines(Guid id, [FromBody] UpdateStockCountLinesRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _counts.UpdateLinesAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockCountDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _counts.SubmitAsync(_tenantContext.TenantId, staffId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockCountDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _counts.ApproveAsync(_tenantContext.TenantId, staffId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockCountDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _counts.CancelAsync(_tenantContext.TenantId, staffId, id);
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
