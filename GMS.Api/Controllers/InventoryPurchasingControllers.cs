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

/// <summary>INVS-5 suppliers + PAP-P0 AP-1 ledger/payment.</summary>
[Route("api/inventory/suppliers")]
[Authorize]
[FeatureFlag("inventory")]
public class InventorySuppliersController : BaseApiController
{
    private readonly ISupplierService _suppliers;
    private readonly ITenantContext _tenantContext;

    public InventorySuppliersController(ISupplierService suppliers, ITenantContext tenantContext)
    {
        _suppliers = suppliers;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<SupplierDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var includeMoney = InventoryCostAccess.CanSeeCost(User);
        var result = await _suppliers.ListAsync(_tenantContext.TenantId, includeInactive, includeMoney);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        if (!includeMoney)
            InventoryCostRedaction.RedactSuppliers(result.Data);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var includeMoney = InventoryCostAccess.CanSeeCost(User);
        var result = await _suppliers.GetAsync(_tenantContext.TenantId, id, includeMoney);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        if (!includeMoney)
            InventoryCostRedaction.RedactSupplier(result.Data);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}/balance")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(SupplierBalanceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Balance(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        if (!InventoryCostAccess.CanSeeCost(User))
            return Forbid();

        var result = await _suppliers.GetBalanceAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}/ledger")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<SupplierLedgerEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ledger(
        Guid id,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        if (!InventoryCostAccess.CanSeeCost(User))
            return Forbid();

        var result = await _suppliers.ListLedgerAsync(_tenantContext.TenantId, id, fromUtc, toUtc);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        var page = result.Data!;
        Response.Headers["X-Gfp-Truncated"] = page.Truncated ? "true" : "false";
        Response.Headers["X-Gfp-Take"] = page.Take.ToString();
        return Ok(page.Items);
    }

    [HttpPost]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateSupplierRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _suppliers.CreateAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        if (!InventoryCostAccess.CanSeeCost(User))
            InventoryCostRedaction.RedactSupplier(result.Data);
        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSupplierRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _suppliers.UpdateAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        if (!InventoryCostAccess.CanSeeCost(User))
            InventoryCostRedaction.RedactSupplier(result.Data);
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/opening")]
    [HasPermission(Permissions.InventoryPurchase)]
    [ProducesResponseType(typeof(SupplierLedgerEntryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> PostOpening(Guid id, [FromBody] PostSupplierOpeningRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _suppliers.PostOpeningAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return StatusCode(StatusCodes.Status201Created, result.Data);
    }

    [HttpPost("{id:guid}/payments")]
    [HasPermission(Permissions.InventoryPurchase)]
    [ProducesResponseType(typeof(SupplierLedgerEntryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> PostPayment(Guid id, [FromBody] PostSupplierPaymentRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _suppliers.PostPaymentAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return StatusCode(StatusCodes.Status201Created, result.Data);
    }
}

/// <summary>INVS-5 purchase orders &amp; goods receipts.</summary>
[Route("api/inventory/purchase-orders")]
[Authorize]
[FeatureFlag("inventory")]
public class InventoryPurchaseOrdersController : BaseApiController
{
    private readonly IPurchaseOrderService _purchaseOrders;
    private readonly ITenantContext _tenantContext;

    public InventoryPurchaseOrdersController(
        IPurchaseOrderService purchaseOrders,
        ITenantContext tenantContext)
    {
        _purchaseOrders = purchaseOrders;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<PurchaseOrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _purchaseOrders.ListAsync(_tenantContext.TenantId, status);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        var page = result.Data!;
        Response.Headers["X-Gfp-Truncated"] = page.Truncated ? "true" : "false";
        Response.Headers["X-Gfp-Take"] = page.Take.ToString();
        if (!InventoryCostAccess.CanSeeCost(User))
            InventoryCostRedaction.RedactPurchaseOrders(page.Items);
        return Ok(page.Items);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _purchaseOrders.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        if (!InventoryCostAccess.CanSeeCost(User))
            InventoryCostRedaction.RedactPurchaseOrder(result.Data);
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreatePurchaseOrderRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _purchaseOrders.CreateDraftAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPost("from-suggestions")]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateFromSuggestions([FromBody] CreatePoFromSuggestionsRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _purchaseOrders.CreateDraftFromSuggestionsAsync(
            _tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.InventoryPurchase)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _purchaseOrders.ApproveAsync(_tenantContext.TenantId, staffId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.InventoryPurchase)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _purchaseOrders.CancelAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/receipts")]
    [HasPermission(Permissions.InventoryPurchase)]
    [ProducesResponseType(typeof(GoodsReceiptDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Receive(Guid id, [FromBody] ReceivePurchaseOrderRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var staffId = GetIdentityUserId();
        if (staffId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _purchaseOrders.ReceiveAsync(
            _tenantContext.TenantId, staffId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return StatusCode(StatusCodes.Status201Created, result.Data);
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

/// <summary>AP-2 Buy docs — list/get Goods Receipts as purchase presentation (PAP-P0 P4).</summary>
[Route("api/inventory/goods-receipts")]
[Authorize]
[FeatureFlag("inventory")]
public class InventoryGoodsReceiptsController : BaseApiController
{
    private readonly IPurchaseOrderService _purchaseOrders;
    private readonly ITenantContext _tenantContext;

    public InventoryGoodsReceiptsController(
        IPurchaseOrderService purchaseOrders,
        ITenantContext tenantContext)
    {
        _purchaseOrders = purchaseOrders;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<GoodsReceiptListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] Guid? supplierId = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _purchaseOrders.ListGoodsReceiptsAsync(
            _tenantContext.TenantId, fromUtc, toUtc, supplierId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        var page = result.Data!;
        Response.Headers["X-Gfp-Truncated"] = page.Truncated ? "true" : "false";
        Response.Headers["X-Gfp-Take"] = page.Take.ToString();
        if (!InventoryCostAccess.CanSeeCost(User))
            InventoryCostRedaction.RedactGoodsReceiptList(page.Items);
        return Ok(page.Items);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(GoodsReceiptDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _purchaseOrders.GetGoodsReceiptAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        if (!InventoryCostAccess.CanSeeCost(User))
            InventoryCostRedaction.RedactGoodsReceipt(result.Data);
        return Ok(result.Data);
    }
}
