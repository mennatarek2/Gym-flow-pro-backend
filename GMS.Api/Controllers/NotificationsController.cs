namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>
/// Notification endpoints for members and staff.
/// Members see their own notifications; staff can send bulk.
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
    /// Get current member's notifications (paginated, newest first).
    /// GET /api/notifications?page=1&amp;pageSize=20
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var memberId = GetMemberId();
        if (memberId == Guid.Empty)
            return Unauthorized(new { error = "Member ID not found in token / معرف العضو غير موجود" });

        var result = await _notificationService.GetMemberNotificationsAsync(memberId, page, pageSize);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error!);
    }

    /// <summary>
    /// Mark a notification as read.
    /// Ownership enforced — member can only mark their own notifications.
    /// POST /api/notifications/{id}/read
    /// </summary>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var memberId = GetMemberId();
        if (memberId == Guid.Empty)
            return Unauthorized(new { error = "Member ID not found in token" });

        var result = await _notificationService.MarkAsReadAsync(id, memberId);

        if (!result.IsSuccess)
        {
            if (result.Error!.Contains("Forbidden"))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = result.Error });
            return NotFound(result.Error);
        }

        return Ok(new { message = result.Data });
    }

    /// <summary>
    /// Send bulk notifications to multiple members or all active members.
    /// Manager+ only.
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
        return result.IsSuccess ? Ok(new { message = result.Data }) : BadRequest(result.Error!);
    }

    // ── Claim Helpers ──

    private Guid GetMemberId()
    {
        // member_id claim is set during login for Member role users
        var memberClaim = User.FindFirst("member_id")?.Value;
        if (Guid.TryParse(memberClaim, out var id)) return id;

        // Fallback: for staff, try sub (user_id) — they won't have member notifications
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var userId) ? userId : Guid.Empty;
    }
}
