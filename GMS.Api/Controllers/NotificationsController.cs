namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>
/// Member inbox + staff bulk send.
/// Member routes resolve GymMember from JWT <c>sub</c> (Identity id) — there is no <c>member_id</c> claim.
/// Staff desk inbox is <c>/api/staff-notifications</c> (not this controller).
/// </summary>
[Route("api/notifications")]
[Authorize]
public class NotificationsController : BaseApiController
{
    private readonly INotificationService _notificationService;
    private readonly ITenantContext _tenantContext;

    public NotificationsController(
        INotificationService notificationService,
        ITenantContext tenantContext)
    {
        _notificationService = notificationService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Member App inbox (paginated, newest first).
    /// GET /api/notifications?page=1&amp;pageSize=20
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "AuthenticatedMember")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var identityUserId = GetIdentityUserId();
        if (identityUserId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _notificationService.GetMemberNotificationsForIdentityAsync(
            _tenantContext.TenantId, identityUserId, page, pageSize);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Member App unread badge.
    /// GET /api/notifications/unread-count
    /// </summary>
    [HttpGet("unread-count")]
    [Authorize(Policy = "AuthenticatedMember")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var identityUserId = GetIdentityUserId();
        if (identityUserId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _notificationService.GetMemberUnreadCountForIdentityAsync(
            _tenantContext.TenantId, identityUserId);
        return result.IsSuccess
            ? Ok(new { count = result.Data })
            : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Mark one notification as read (own rows only).
    /// POST /api/notifications/{id}/read
    /// </summary>
    [HttpPost("{id:guid}/read")]
    [Authorize(Policy = "AuthenticatedMember")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var identityUserId = GetIdentityUserId();
        if (identityUserId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _notificationService.MarkAsReadForIdentityAsync(
            _tenantContext.TenantId, identityUserId, id);

        if (!result.IsSuccess)
        {
            if (result.Error!.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = result.Error });
            if (result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { error = result.Error });
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { message = result.Data });
    }

    /// <summary>
    /// Mark all member notifications as read.
    /// POST /api/notifications/read-all
    /// </summary>
    [HttpPost("read-all")]
    [Authorize(Policy = "AuthenticatedMember")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllAsRead()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var identityUserId = GetIdentityUserId();
        if (identityUserId == Guid.Empty)
            return Unauthorized(new { error = "Please log in / يرجى تسجيل الدخول" });

        var result = await _notificationService.MarkAllAsReadForIdentityAsync(
            _tenantContext.TenantId, identityUserId);
        return result.IsSuccess
            ? Ok(new { message = result.Data })
            : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Send bulk notifications to members (Manager+). Desk compose tool — not Member App.
    /// POST /api/notifications/send-bulk
    /// </summary>
    [HttpPost("send-bulk")]
    [Authorize(Policy = "ManagerOrAbove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendBulk([FromBody] SendBulkNotificationRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _notificationService.SendBulkNotificationAsync(tenantId, request);
        return result.IsSuccess ? Ok(new { message = result.Data }) : BadRequest(new { error = result.Error });
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
