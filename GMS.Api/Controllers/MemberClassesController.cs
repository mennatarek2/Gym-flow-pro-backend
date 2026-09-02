namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Activities;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>
/// Read-only Member App classes. Browse upcoming class sessions and view details.
/// Does not create bookings, payments, or seat reservations.
/// </summary>
[Route("api/member/classes")]
[Authorize(Policy = "AuthenticatedMember")]
public class MemberClassesController : BaseApiController
{
    private readonly IMemberClassService _classes;
    private readonly ITenantContext _tenantContext;

    public MemberClassesController(IMemberClassService classes, ITenantContext tenantContext)
    {
        _classes = classes;
        _tenantContext = tenantContext;
    }

    /// <summary>Upcoming class sessions visible to members (information only).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<MemberClassListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? activityId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _classes.ListUpcomingAsync(
            _tenantContext.TenantId, userId, activityId, fromUtc, limit, ct);

        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    /// <summary>Class session details by session id (information only).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MemberClassDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct = default)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _classes.GetByIdAsync(_tenantContext.TenantId, userId, id, ct);
        return result.IsSuccess ? Ok(result.Data) : NotFound(new { error = result.Error });
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
