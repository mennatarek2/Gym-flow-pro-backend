namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Filters;
using GMS.Application.DTOs.MemberStore;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>
/// Member App "My Orders" — always scoped to the authenticated member.
/// Prefer this route for Profile → Orders. Do NOT call staff <c>/api/member-orders</c>.
/// Feature-gated: inventory.
/// </summary>
[Route("api/member/orders")]
[Authorize(Policy = "AuthenticatedMember")]
[FeatureFlag("inventory")]
public class MemberMyOrdersController : BaseApiController
{
    private readonly IMemberStoreService _store;
    private readonly ITenantContext _tenantContext;

    public MemberMyOrdersController(IMemberStoreService store, ITenantContext tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Lists the authenticated member's store orders only.
    /// A client-supplied <c>memberId</c> query value is ignored — JWT identity is authoritative.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<MemberOrderListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? memberId,
        CancellationToken ct)
    {
        // memberId query is intentionally unused (defense against IDOR / confused clients).
        _ = memberId;

        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _store.ListMyOrdersAsync(_tenantContext.TenantId, userId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Order detail for the authenticated member only. Another member's order id → 404 (no leak).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MemberOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
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
