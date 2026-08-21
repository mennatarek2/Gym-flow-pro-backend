namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Filters;
using GMS.Application.DTOs.MemberStore;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>Member App store catalog + my orders. Feature-gated: inventory.</summary>
[Route("api/member-store")]
[Authorize(Policy = "AuthenticatedMember")]
[FeatureFlag("inventory")]
public class MemberStoreController : BaseApiController
{
    private readonly IMemberStoreService _store;
    private readonly ITenantContext _tenantContext;

    public MemberStoreController(IMemberStoreService store, ITenantContext tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    [HttpGet("products")]
    [ProducesResponseType(typeof(List<MemberStoreProductDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProducts([FromQuery] string? q, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _store.ListStoreProductsAsync(_tenantContext.TenantId, q, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpPost("orders")]
    [ProducesResponseType(typeof(MemberOrderDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateOrder([FromBody] CreateMemberOrderRequest request, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _store.CreateOrderAsync(_tenantContext.TenantId, userId, request, ct);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(GetMyOrder), new { id = result.Data!.Id }, result.Data);
    }

    [HttpGet("orders")]
    [ProducesResponseType(typeof(List<MemberOrderListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMyOrders(CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _store.ListMyOrdersAsync(_tenantContext.TenantId, userId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpGet("orders/{id:guid}")]
    [ProducesResponseType(typeof(MemberOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyOrder(Guid id, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _store.GetMyOrderAsync(_tenantContext.TenantId, userId, id, ct);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
