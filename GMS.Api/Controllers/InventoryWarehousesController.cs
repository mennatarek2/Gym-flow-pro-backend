namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>INVS-2 warehouses. Feature-gated: inventory.</summary>
[Route("api/inventory/warehouses")]
[Authorize]
[FeatureFlag("inventory")]
public class InventoryWarehousesController : BaseApiController
{
    private readonly IWarehouseService _warehouses;
    private readonly ITenantContext _tenantContext;

    public InventoryWarehousesController(IWarehouseService warehouses, ITenantContext tenantContext)
    {
        _warehouses = warehouses;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<WarehouseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _warehouses.ListAsync(_tenantContext.TenantId, includeInactive);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpGet("default")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(WarehouseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDefault()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _warehouses.GetOrCreateDefaultAsync(_tenantContext.TenantId);
        if (!result.IsSuccess || result.Data == null)
            return BadRequest(new { error = result.Error ?? "Unable to resolve default warehouse" });

        var wh = result.Data;
        return Ok(new WarehouseDto
        {
            Id = wh.Id,
            Code = wh.Code,
            Name = wh.Name,
            NameAr = wh.NameAr,
            IsDefault = wh.IsDefault,
            IsActive = wh.IsActive,
            BranchId = wh.BranchId,
            CreatedAtUtc = wh.CreatedAtUtc,
            UpdatedAtUtc = wh.UpdatedAtUtc
        });
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(WarehouseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _warehouses.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(WarehouseDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateWarehouseRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _warehouses.CreateAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(WarehouseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWarehouseRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _warehouses.UpdateAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/set-default")]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(WarehouseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDefault(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _warehouses.SetDefaultAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }
}
