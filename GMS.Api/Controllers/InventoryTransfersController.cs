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

/// <summary>INVS-8 warehouse transfers.</summary>
[Route("api/inventory/transfers")]
[Authorize]
[FeatureFlag("stock_management")]
public class InventoryTransfersController : BaseApiController
{
    private readonly IStockTransferService _transfers;
    private readonly ITenantContext _tenantContext;

    public InventoryTransfersController(IStockTransferService transfers, ITenantContext tenantContext)
    {
        _transfers = transfers;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.InventoryTransfer)]
    [ProducesResponseType(typeof(List<StockTransferDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _transfers.ListAsync(_tenantContext.TenantId, status);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        var page = result.Data!;
        Response.Headers["X-Gfp-Truncated"] = page.Truncated ? "true" : "false";
        Response.Headers["X-Gfp-Take"] = page.Take.ToString();
        return Ok(page.Items);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InventoryTransfer)]
    [ProducesResponseType(typeof(StockTransferDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _transfers.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.InventoryTransfer)]
    [ProducesResponseType(typeof(StockTransferDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateStockTransferRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _transfers.CreatePendingAsync(_tenantContext.TenantId, staffId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.InventoryTransfer)]
    [ProducesResponseType(typeof(StockTransferDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _transfers.SubmitAsync(_tenantContext.TenantId, staffId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/receive")]
    [HasPermission(Permissions.InventoryTransfer)]
    [ProducesResponseType(typeof(StockTransferDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Receive(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _transfers.ReceiveAsync(_tenantContext.TenantId, staffId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.InventoryTransfer)]
    [ProducesResponseType(typeof(StockTransferDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _transfers.CancelAsync(_tenantContext.TenantId, staffId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.InventoryTransfer)]
    [ProducesResponseType(typeof(StockTransferDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _transfers.RejectAsync(_tenantContext.TenantId, staffId, id);
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
