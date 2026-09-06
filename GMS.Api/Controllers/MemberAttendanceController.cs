namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.Common;
using GMS.Application.DTOs.Members;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>
/// Member App visit history — always scoped to the authenticated member.
/// Prefer this route for Profile → Attendance. Do NOT call staff
/// <c>/api/members/{id}/attendance</c> or the non-existent <c>/api/attendance/history</c>.
/// </summary>
[Route("api/member/attendance")]
[Authorize(Policy = "AuthenticatedMember")]
public class MemberAttendanceController : BaseApiController
{
    private readonly IMemberService _members;
    private readonly ITenantContext _tenantContext;

    public MemberAttendanceController(IMemberService members, ITenantContext tenantContext)
    {
        _members = members;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Lists the authenticated member's gym visits only (newest first).
    /// A client-supplied <c>memberId</c> query value is ignored — JWT identity is authoritative.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AttendanceSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? memberId = null)
    {
        // memberId query is intentionally unused (defense against IDOR / confused clients).
        _ = memberId;

        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var userId = GetIdentityUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _members.GetMyAttendanceAsync(
            _tenantContext.TenantId, userId, page, pageSize);

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
