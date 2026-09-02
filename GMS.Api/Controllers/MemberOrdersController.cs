namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.MemberStore;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>Staff inbox for member-store operational orders. Feature-gated: inventory.</summary>
[Route("api/member-orders")]
[Authorize]
[FeatureFlag("inventory")]
public class MemberOrdersController : BaseApiController
{
    private readonly IMemberStoreService _store;
    private readonly ITenantContext _tenantContext;

    public MemberOrdersController(IMemberStoreService store, ITenantContext tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.MemberOrdersView)]
    [ProducesResponseType(typeof(List<MemberOrderListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] Guid? memberId,
        CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _store.ListOrdersForStaffAsync(
            _tenantContext.TenantId, status, memberId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MemberOrdersView)]
    [ProducesResponseType(typeof(MemberOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _store.GetOrderForStaffAsync(_tenantContext.TenantId, id, ct);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/accept")]
    [HasPermission(Permissions.MemberOrdersManage)]
    [ProducesResponseType(typeof(MemberOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Staff identity required." });

        var result = await _store.AcceptAsync(_tenantContext.TenantId, id, userId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.MemberOrdersManage)]
    [ProducesResponseType(typeof(MemberOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectMemberOrderRequest? request, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Staff identity required." });

        var result = await _store.RejectAsync(
            _tenantContext.TenantId, id, userId, request ?? new RejectMemberOrderRequest(), ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/ready")]
    [HasPermission(Permissions.MemberOrdersManage)]
    [ProducesResponseType(typeof(MemberOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ready(Guid id, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Staff identity required." });

        var result = await _store.MarkReadyAsync(_tenantContext.TenantId, id, userId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/complete")]
    [HasPermission(Permissions.MemberOrdersManage)]
    [ProducesResponseType(typeof(MemberOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Staff identity required." });

        var result = await _store.CompleteAsync(_tenantContext.TenantId, id, userId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
