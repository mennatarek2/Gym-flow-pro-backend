namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Invitation;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Invitation product — member create + staff follow-up.
/// JWT <c>sub</c> is Identity id; GymMember is resolved server-side.
/// </summary>
[Route("api/invitation")]
public class InvitationController : BaseApiController
{
    private readonly IInvitationService _invitationService;
    private readonly ITenantContext _tenantContext;

    public InvitationController(IInvitationService invitationService, ITenantContext tenantContext)
    {
        _invitationService = invitationService;
        _tenantContext = tenantContext;
    }

    [HttpPost("send")]
    [Authorize(Policy = "AuthenticatedMember")]
    [ProducesResponseType(typeof(SendInvitationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendInvitation([FromBody] SendInvitationRequest request)
    {
        var memberId = GetIdentityUserId();
        if (memberId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _invitationService.SendInvitationAsync(
            request, memberId, _tenantContext.TenantId);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    /// <summary>Front desk: create an invitation for a member. GymMember.Id in the URL.</summary>
    [HttpPost("members/{memberId:guid}")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(SendInvitationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendInvitationForMember(
        Guid memberId, [FromBody] SendInvitationRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _invitationService.SendInvitationForMemberAsync(
            request, memberId, _tenantContext.TenantId);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    [HttpGet("history")]
    [Authorize(Policy = "AuthenticatedMember")]
    [ProducesResponseType(typeof(List<InvitationHistoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory()
    {
        var memberId = GetIdentityUserId();
        if (memberId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _invitationService.GetMemberInvitationsAsync(
            memberId, _tenantContext.TenantId);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    /// <summary>Member App quota meter. Backend calculates total / used / remaining.</summary>
    [HttpGet("summary")]
    [Authorize(Policy = "AuthenticatedMember")]
    [ProducesResponseType(typeof(InvitationQuotaDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMySummary()
    {
        var identityUserId = GetIdentityUserId();
        if (identityUserId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _invitationService.GetMyInvitationSummaryAsync(
            identityUserId, _tenantContext.TenantId);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    [HttpGet]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(List<InvitationHistoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStaffList([FromQuery] string? status, [FromQuery] string? q)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _invitationService.GetStaffInvitationsAsync(
            _tenantContext.TenantId, status, q);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    [HttpGet("members/{memberId:guid}")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(InvitationMemberSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMember360(Guid memberId)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _invitationService.GetMemberInvitation360Async(
            memberId, _tenantContext.TenantId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    [HttpPatch("{id:guid}/status")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(InvitationHistoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateInvitationStatusRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _invitationService.UpdateInvitationStatusAsync(
            id, _tenantContext.TenantId, request.Status);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var parsed) ? parsed : Guid.Empty;
    }
}
