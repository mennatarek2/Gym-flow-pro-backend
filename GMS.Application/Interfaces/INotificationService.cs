namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Notifications;

/// <summary>
/// Notification management service (member inbox + staff inbox + bulk send).
/// </summary>
public interface INotificationService
{
    /// <summary>Get paginated notifications for a GymMember.Id.</summary>
    Task<Result<PagedResult<NotificationDto>>> GetMemberNotificationsAsync(
        Guid memberId, int page, int pageSize);

    /// <summary>
    /// Member App: resolve GymMember from Identity <c>sub</c> + tenant, then list inbox.
    /// </summary>
    Task<Result<PagedResult<NotificationDto>>> GetMemberNotificationsForIdentityAsync(
        Guid tenantId, Guid identityUserId, int page, int pageSize);

    /// <summary>Unread count for Member App badge (Identity <c>sub</c> + tenant).</summary>
    Task<Result<int>> GetMemberUnreadCountForIdentityAsync(Guid tenantId, Guid identityUserId);

    /// <summary>Mark a notification as read (member ownership enforced via GymMember.Id).</summary>
    Task<Result<string>> MarkAsReadAsync(Guid notificationId, Guid memberId);

    /// <summary>Member App mark-read via Identity <c>sub</c>.</summary>
    Task<Result<string>> MarkAsReadForIdentityAsync(
        Guid tenantId, Guid identityUserId, Guid notificationId);

    /// <summary>Member App mark-all-read via Identity <c>sub</c>.</summary>
    Task<Result<string>> MarkAllAsReadForIdentityAsync(Guid tenantId, Guid identityUserId);

    /// <summary>Send bulk notifications via push or WhatsApp.</summary>
    Task<Result<string>> SendBulkNotificationAsync(
        Guid tenantId, SendBulkNotificationRequest request);

    /// <summary>Creates a single in-app notification for a staff member (app_users.Id).</summary>
    /// <param name="externalMessageId">Optional once-per-day / provider dedupe key (stored on Notification.ExternalMessageId).</param>
    Task<Result<string>> CreateForStaffAsync(
        Guid tenantId, Guid appUserId, string title, string titleAr, string body, string bodyAr,
        string? externalMessageId = null,
        string? type = null, string? category = null, string? priority = null,
        string? entityType = null, Guid? entityId = null, string? actionUrl = null,
        DateTime? expiresAtUtc = null);

    Task<Result<PagedResult<StaffNotificationDto>>> GetStaffNotificationsAsync(
        Guid tenantId, Guid appUserId, int page, int pageSize, bool unreadOnly = false, string? category = null);

    Task<Result<int>> GetStaffUnreadCountAsync(Guid tenantId, Guid appUserId);

    Task<Result<string>> MarkStaffAsReadAsync(Guid tenantId, Guid appUserId, Guid notificationId);

    Task<Result<string>> MarkAllStaffAsReadAsync(Guid tenantId, Guid appUserId);

    /// <summary>Resolve recipients, apply tenant-level dedupe, persist staff notifications.</summary>
    Task<Result<int>> PublishStaffAsync(Guid tenantId, CreateStaffNotificationRequest request);
}
