namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Memberships;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>
/// REST API controller for membership management.
/// Handles membership lifecycle: creation, renewal, history, current status.
/// All operations automatically scoped to current tenant.
/// </summary>
[Route("api/memberships")]
[Authorize]
public class MembershipsController : BaseApiController
{
    private readonly IMembershipService _membershipService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<MembershipsController> _logger;

    public MembershipsController(
        IMembershipService membershipService,
        ITenantContext tenantContext,
        ILogger<MembershipsController> logger)
    {
        _membershipService = membershipService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get current active membership for a member.
    /// If no active membership, returns the last expired one.
    /// GET /api/memberships/{memberId}/current
    /// </summary>
    [HttpGet("{memberId:guid}/current")]
    [Authorize(Policy = "AnyStaff")]
    [ProducesResponseType(typeof(MembershipDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentMembership(Guid memberId)
    {
        var result = await _membershipService.GetCurrentMembershipAsync(memberId);
        return result.IsSuccess ? Ok(result.Data) : NotFound(result.Error!);
    }

    /// <summary>
    /// Get membership history for a member (paginated, newest first).
    /// GET /api/memberships/{memberId}/history?page=1&pageSize=20
    /// </summary>
    [HttpGet("{memberId:guid}/history")]
    [Authorize(Policy = "AnyStaff")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMembershipHistory(
        Guid memberId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _membershipService.GetMembershipHistoryAsync(memberId, page, pageSize);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error!);
    }

    /// <summary>
    /// Assign a new membership to a member.
    /// 
    /// If PaymentMethod is 'cash': membership created immediately (status = active)
    /// If PaymentMethod is payment gateway (paymob, fawry):
    ///   - Membership created with status = 'pending'
    ///   - Webhook will activate membership on successful payment
    /// 
    /// Fails if member already has active membership.
    /// 
    /// POST /api/memberships/{memberId}/assign
    /// </summary>
    [HttpPost("{memberId:guid}/assign")]
    [Authorize(Policy = "ManagerOrAbove")]
    [ProducesResponseType(typeof(MembershipDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignMembership(
        Guid memberId,
        [FromBody] AssignMembershipRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _membershipService.AssignMembershipAsync(
            tenantId, memberId, request, GetUserId());

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to assign membership: {Error}", result.Error);

            // Return 409 if member already has active membership
            if (result.Error?.Contains("active membership") ?? false)
                return Conflict(new { error = result.Error, message = result.Message });

            return BadRequest(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation(
            "Membership assigned: MemberId={MemberId}, PlanId={PlanId}, PaymentMethod={PaymentMethod}",
            memberId, request.PlanId, request.PaymentMethod);

        return CreatedAtAction(
            nameof(GetCurrentMembership),
            new { memberId = memberId },
            result.Data);
    }

    /// <summary>
    /// Renew member's current/expired membership.
    /// Body may include transitionMode: cancel_and_switch (default) | queue_next | manual_rollover.
    /// Cash requires an open shift and registers Sale + shift cash movement.
    /// POST /api/memberships/{memberId}/renew
    /// </summary>
    [HttpPost("{memberId:guid}/renew")]
    [Authorize(Policy = "ManagerOrAbove")]
    [ProducesResponseType(typeof(MembershipDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RenewMembership(
        Guid memberId,
        [FromBody] RenewMembershipRequest request)
    {
        var tenantId = _tenantContext.TenantId;

        var result = await _membershipService.RenewMembershipAsync(
            tenantId, memberId, request, GetUserId());

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to renew membership: {Error}", result.Error);
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation(
            "Membership renewed: MemberId={MemberId}, PaymentMethod={PaymentMethod}",
            memberId, request.PaymentMethod);

        return Ok(result.Data);
    }

    /// <summary>
    /// Stop the current operational plan (active / frozen / scheduled / pending).
    /// Not a refund — cash stays; remaining on this plan is not collected.
    /// POST /api/memberships/{memberId}/cancel
    /// </summary>
    [HttpPost("{memberId:guid}/cancel")]
    [Authorize(Policy = "ManagerOrAbove")]
    [ProducesResponseType(typeof(MembershipDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelMembership(Guid memberId)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _membershipService.CancelMembershipAsync(
            tenantId, memberId, GetUserId());

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to cancel membership: {Error}", result.Error);
            if (result.Error?.Contains("cannot be cancelled", StringComparison.OrdinalIgnoreCase) ?? false)
                return Conflict(new { error = result.Error, message = result.Message });
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation("Membership cancelled: MemberId={MemberId}", memberId);
        return Ok(result.Data);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
