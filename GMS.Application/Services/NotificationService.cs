namespace GMS.Application.Services;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Notification service for in-app, push, and WhatsApp notifications.
/// - Records every notification in the DB for history
/// - Sends via appropriate channel (FCM / WhatsApp)
/// - Supports bulk send to all active members or specific IDs
/// </summary>
public class NotificationService : INotificationService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IPushNotificationService _pushService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        GymFlowProDbContext dbContext,
        IPushNotificationService pushService,
        ILogger<NotificationService> logger)
    {
        _dbContext = dbContext;
        _pushService = pushService;
        _logger = logger;
    }

    public async Task<Result<PagedResult<NotificationDto>>> GetMemberNotificationsAsync(
        Guid memberId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _dbContext.Set<Notification>()
            .Where(n => n.MemberId == memberId && !n.IsDeleted)
            .OrderByDescending(n => n.SentAtUtc ?? n.CreatedAtUtc);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                Title = n.Title,
                TitleAr = n.TitleAr,
                Body = n.Body,
                BodyAr = n.BodyAr,
                Channel = n.Channel,
                SentAt = n.SentAtUtc,
                IsRead = n.ReadAtUtc != null
            })
            .ToListAsync();

        return Result<PagedResult<NotificationDto>>.Success(new PagedResult<NotificationDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<Result<string>> MarkAsReadAsync(Guid notificationId, Guid memberId)
    {
        var notification = await _dbContext.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == notificationId && !n.IsDeleted);

        if (notification == null)
            return Result<string>.Failure("Notification not found / الإشعار غير موجود");

        // Ownership check — member can only mark their own notifications
        if (notification.MemberId != memberId)
            return Result<string>.Failure("Forbidden: this notification belongs to another member / غير مسموح");

        if (notification.ReadAtUtc != null)
            return Result<string>.Success("Already read / تمت القراءة مسبقاً");

        notification.ReadAtUtc = DateTime.UtcNow;
        notification.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Result<string>.Success("Notification marked as read / تم وضع علامة مقروء");
    }

    public async Task<Result<string>> SendBulkNotificationAsync(
        Guid tenantId, SendBulkNotificationRequest request)
    {
        // Resolve target members
        List<GymMember> targetMembers;

        if (request.AllMembers)
        {
            targetMembers = await _dbContext.GymMembers
                .Where(m => m.TenantId == tenantId && m.IsActive)
                .ToListAsync();
        }
        else if (request.MemberIds != null && request.MemberIds.Count > 0)
        {
            targetMembers = await _dbContext.GymMembers
                .Where(m => m.TenantId == tenantId && request.MemberIds.Contains(m.Id) && m.IsActive)
                .ToListAsync();
        }
        else
        {
            return Result<string>.Failure(
                "Specify MemberIds or set AllMembers=true / حدد الأعضاء أو اختار الكل");
        }

        if (targetMembers.Count == 0)
            return Result<string>.Failure("No active members found / لا يوجد أعضاء نشطين");

        var notifications = new List<Notification>();
        var now = DateTime.UtcNow;

        foreach (var member in targetMembers)
        {
            var notification = new Notification
            {
                TenantId = tenantId,
                MemberId = member.Id,
                Channel = request.Channel.ToLowerInvariant(),
                Title = request.Title,
                TitleAr = request.TitleAr,
                Body = request.Body,
                BodyAr = request.BodyAr,
                Status = "sent",
                SentAtUtc = now
            };
            notifications.Add(notification);
        }

        await _dbContext.Set<Notification>().AddRangeAsync(notifications);
        await _dbContext.SaveChangesAsync();

        // Enqueue actual delivery as background jobs
        foreach (var member in targetMembers)
        {
            if (request.Channel.Equals("whatsapp", StringComparison.OrdinalIgnoreCase))
            {
                // Fire-and-forget WhatsApp via Hangfire
                var phone = member.PhoneNumber;
                var body = request.Body;
                BackgroundJob.Enqueue<IWhatsAppService>(svc =>
                    svc.SendExpiryReminderAsync(phone, member.FullName, 0));
            }
            else // push
            {
                // Topic-based push: gym-{tenantId}
                // For individual: would need FCM token stored on member
                BackgroundJob.Enqueue<IPushNotificationService>(svc =>
                    svc.SendToTopicAsync($"gym-{tenantId}", request.Title, request.Body));
            }
        }

        _logger.LogInformation(
            "[Notification] Bulk sent: {Count} {Channel} notifications in tenant {TenantId}",
            targetMembers.Count, request.Channel, tenantId);

        return Result<string>.Success(
            $"Sent {targetMembers.Count} notifications / تم إرسال {targetMembers.Count} إشعار");
    }

    public async Task<Result<string>> CreateForStaffAsync(
        Guid tenantId, Guid appUserId, string title, string titleAr, string body, string bodyAr,
        string? externalMessageId = null)
    {
        var notification = new Notification
        {
            TenantId = tenantId,
            AppUserId = appUserId,
            Channel = "in_app",
            Title = title,
            TitleAr = titleAr,
            Body = body,
            BodyAr = bodyAr,
            Status = "sent",
            SentAtUtc = DateTime.UtcNow,
            ExternalMessageId = string.IsNullOrWhiteSpace(externalMessageId)
                ? null
                : externalMessageId.Trim()
        };

        _dbContext.Set<Notification>().Add(notification);
        await _dbContext.SaveChangesAsync();

        return Result<string>.Success("Notification created / تم إنشاء الإشعار");
    }
}
