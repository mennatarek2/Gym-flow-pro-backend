namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Notifications;

/// <summary>
/// Notification management service.
/// </summary>
public interface INotificationService
{
    /// <summary>Get paginated notifications for a member.</summary>
    Task<Result<PagedResult<NotificationDto>>> GetMemberNotificationsAsync(
        Guid memberId, int page, int pageSize);

    /// <summary>Mark a notification as read (member ownership enforced).</summary>
    Task<Result<string>> MarkAsReadAsync(Guid notificationId, Guid memberId);

    /// <summary>Send bulk notifications via push or WhatsApp.</summary>
    Task<Result<string>> SendBulkNotificationAsync(
        Guid tenantId, SendBulkNotificationRequest request);

    /// <summary>Creates a single in-app notification for a staff member (app_users.Id).</summary>
    /// <param name="externalMessageId">Optional once-per-day / provider dedupe key (stored on Notification.ExternalMessageId).</param>
    Task<Result<string>> CreateForStaffAsync(
        Guid tenantId, Guid appUserId, string title, string titleAr, string body, string bodyAr,
        string? externalMessageId = null);
}
