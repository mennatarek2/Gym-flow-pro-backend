namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>Staff in-app notification inbox. Recipient always comes from the authenticated AppUser.</summary>
[Route("api/staff-notifications")]
[Authorize]
public class StaffNotificationsController : BaseApiController
{
    private readonly INotificationService _notifications;
    private readonly ITenantContext _tenantContext;
    private readonly GymFlowProDbContext _db;

    public StaffNotificationsController(
        INotificationService notifications,
        ITenantContext tenantContext,
        GymFlowProDbContext db)
    {
        _notifications = notifications;
        _tenantContext = tenantContext;
        _db = db;
    }

    [HttpGet]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool unreadOnly = false,
        [FromQuery] string? category = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var appUserId = await ResolveAppUserIdAsync();
        if (appUserId == null)
            return Unauthorized(new { error = "Staff account required / يلزم حساب موظف" });

        var result = await _notifications.GetStaffNotificationsAsync(
            _tenantContext.TenantId, appUserId.Value, page, pageSize, unreadOnly, category);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(StaffUnreadCountDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var appUserId = await ResolveAppUserIdAsync();
        if (appUserId == null)
            return Unauthorized(new { error = "Staff account required / يلزم حساب موظف" });

        var result = await _notifications.GetStaffUnreadCountAsync(_tenantContext.TenantId, appUserId.Value);
        return result.IsSuccess
            ? Ok(new StaffUnreadCountDto { Count = result.Data })
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var appUserId = await ResolveAppUserIdAsync();
        if (appUserId == null)
            return Unauthorized(new { error = "Staff account required / يلزم حساب موظف" });

        var result = await _notifications.MarkStaffAsReadAsync(_tenantContext.TenantId, appUserId.Value, id);
        if (!result.IsSuccess)
        {
            if (result.Error!.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = result.Error });
            return NotFound(new { error = result.Error });
        }

        return Ok(new { message = result.Data });
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllRead()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var appUserId = await ResolveAppUserIdAsync();
        if (appUserId == null)
            return Unauthorized(new { error = "Staff account required / يلزم حساب موظف" });

        var result = await _notifications.MarkAllStaffAsReadAsync(_tenantContext.TenantId, appUserId.Value);
        return result.IsSuccess ? Ok(new { message = result.Data }) : BadRequest(new { error = result.Error });
    }

    private async Task<Guid?> ResolveAppUserIdAsync()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(sub))
            return null;

        var user = await _db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u =>
                u.TenantId == _tenantContext.TenantId
                && u.UserId == sub
                && u.IsActive
                && !u.IsDeleted);
        return user?.Id;
    }
}
