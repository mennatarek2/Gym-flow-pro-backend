namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Offers;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>Member App offers. Server already filters visibility, dates, and new-member eligibility.</summary>
[Route("api/member/offers")]
[Authorize(Policy = "AuthenticatedMember")]
public class MemberOffersController : BaseApiController
{
    private readonly IOfferService _offers;
    private readonly ITenantContext _tenantContext;

    public MemberOffersController(IOfferService offers, ITenantContext tenantContext)
    {
        _offers = offers;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<MemberOfferDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _offers.ListMemberAsync(_tenantContext.TenantId, userId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MemberOfferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _offers.GetMemberByIdAsync(_tenantContext.TenantId, userId, id, ct);
        return result.IsSuccess ? Ok(result.Data) : NotFound(new { error = result.Error });
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
