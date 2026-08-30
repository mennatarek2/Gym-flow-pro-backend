namespace GMS.Application.Services;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

/// <summary>
/// Notification service for in-app, push, and WhatsApp notifications.
/// Member rows use MemberId; staff inbox rows use AppUserId plus typed metadata.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IPushNotificationService _pushService;
    private readonly IPermissionProvider _permissionProvider;
    private readonly IStaffNotificationRealtimeNotifier _realtime;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        GymFlowProDbContext dbContext,
        IPushNotificationService pushService,
        ILogger<NotificationService> logger,
        IPermissionProvider? permissionProvider = null,
        IStaffNotificationRealtimeNotifier? realtime = null)
    {
        _dbContext = dbContext;
        _pushService = pushService;
        _logger = logger;
        _permissionProvider = permissionProvider ?? new DefaultPermissionProvider();
        _realtime = realtime ?? new NullStaffNotificationRealtimeNotifier();
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

    public async Task<Result<PagedResult<NotificationDto>>> GetMemberNotificationsForIdentityAsync(
        Guid tenantId, Guid identityUserId, int page, int pageSize)
    {
        var member = await FindMemberByIdentityAsync(tenantId, identityUserId);
        if (member == null)
            return Result<PagedResult<NotificationDto>>.Failure(
                "Member profile not linked / ملف العضو غير مرتبط");

        return await GetMemberNotificationsAsync(member.Id, page, pageSize);
    }

    public async Task<Result<int>> GetMemberUnreadCountForIdentityAsync(Guid tenantId, Guid identityUserId)
    {
        var member = await FindMemberByIdentityAsync(tenantId, identityUserId);
        if (member == null)
            return Result<int>.Failure("Member profile not linked / ملف العضو غير مرتبط");

        var count = await _dbContext.Set<Notification>()
            .CountAsync(n => n.MemberId == member.Id && !n.IsDeleted && n.ReadAtUtc == null);
        return Result<int>.Success(count);
    }

    public async Task<Result<string>> MarkAsReadAsync(Guid notificationId, Guid memberId)
    {
        var notification = await _dbContext.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == notificationId && !n.IsDeleted);

        if (notification == null)
            return Result<string>.Failure("Notification not found / الإشعار غير موجود");

        if (notification.MemberId != memberId)
            return Result<string>.Failure("Forbidden: this notification belongs to another member / غير مسموح");

        if (notification.ReadAtUtc != null)
            return Result<string>.Success("Already read / تمت القراءة مسبقاً");

        notification.ReadAtUtc = DateTime.UtcNow;
        notification.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Result<string>.Success("Notification marked as read / تم وضع علامة مقروء");
    }

    public async Task<Result<string>> MarkAsReadForIdentityAsync(
        Guid tenantId, Guid identityUserId, Guid notificationId)
    {
        var member = await FindMemberByIdentityAsync(tenantId, identityUserId);
        if (member == null)
            return Result<string>.Failure("Member profile not linked / ملف العضو غير مرتبط");

        return await MarkAsReadAsync(notificationId, member.Id);
    }

    public async Task<Result<string>> MarkAllAsReadForIdentityAsync(Guid tenantId, Guid identityUserId)
    {
        var member = await FindMemberByIdentityAsync(tenantId, identityUserId);
        if (member == null)
            return Result<string>.Failure("Member profile not linked / ملف العضو غير مرتبط");

        var unread = await _dbContext.Set<Notification>()
            .Where(n => n.MemberId == member.Id && !n.IsDeleted && n.ReadAtUtc == null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.ReadAtUtc = now;
            n.UpdatedAtUtc = now;
        }

        if (unread.Count > 0)
            await _dbContext.SaveChangesAsync();

        return Result<string>.Success($"Marked {unread.Count} as read / تم تعليم {unread.Count} كمقروء");
    }

    private async Task<GymMember?> FindMemberByIdentityAsync(Guid tenantId, Guid identityUserId)
    {
        var identityKey = identityUserId.ToString();
        return await _dbContext.GymMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.TenantId == tenantId
                && m.AppUser != null
                && m.AppUser.UserId == identityKey);
    }

    public async Task<Result<string>> SendBulkNotificationAsync(
        Guid tenantId, SendBulkNotificationRequest request)
    {
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
            notifications.Add(new Notification
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
            });
        }

        await _dbContext.Set<Notification>().AddRangeAsync(notifications);
        await _dbContext.SaveChangesAsync();

        // Capture form content once — Hangfire serializes these args per job.
        var title = request.Title ?? string.Empty;
        var titleAr = request.TitleAr ?? string.Empty;
        var body = request.Body ?? string.Empty;
        var bodyAr = request.BodyAr ?? string.Empty;
        var isWhatsApp = request.Channel.Equals("whatsapp", StringComparison.OrdinalIgnoreCase);

        foreach (var member in targetMembers)
        {
            if (isWhatsApp)
            {
                // Generic desk broadcast — exact Title/Body. Do NOT use SendExpiryReminderAsync.
                var phone = member.PhoneNumber ?? string.Empty;
                BackgroundJob.Enqueue<IWhatsAppService>(svc =>
                    svc.SendNotificationAsync(phone, title, body, titleAr, bodyAr));
            }
            else
            {
                BackgroundJob.Enqueue<IPushNotificationService>(svc =>
                    svc.SendToTopicAsync($"gym-{tenantId}", title, body));
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
        string? externalMessageId = null,
        string? type = null, string? category = null, string? priority = null,
        string? entityType = null, Guid? entityId = null, string? actionUrl = null,
        DateTime? expiresAtUtc = null)
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
                : externalMessageId.Trim(),
            Type = type,
            Category = category,
            Priority = priority,
            EntityType = entityType,
            EntityId = entityId,
            ActionUrl = actionUrl,
            ExpiresAtUtc = expiresAtUtc
        };

        _dbContext.Set<Notification>().Add(notification);
        await _dbContext.SaveChangesAsync();

        try
        {
            await _realtime.NotifyCreatedAsync(tenantId, new[] { appUserId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Staff notification realtime push failed for tenant {TenantId}", tenantId);
        }

        return Result<string>.Success("Notification created / تم إنشاء الإشعار");
    }

    public async Task<Result<PagedResult<StaffNotificationDto>>> GetStaffNotificationsAsync(
        Guid tenantId, Guid appUserId, int page, int pageSize, bool unreadOnly = false, string? category = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var now = DateTime.UtcNow;
        var query = _dbContext.Set<Notification>().AsNoTracking()
            .Where(n => n.TenantId == tenantId
                && n.AppUserId == appUserId
                && !n.IsDeleted
                && (n.ExpiresAtUtc == null || n.ExpiresAtUtc > now));

        if (unreadOnly)
            query = query.Where(n => n.ReadAtUtc == null);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(n => n.Category == category);

        query = query.OrderByDescending(n => n.SentAtUtc ?? n.CreatedAtUtc);

        var totalCount = await query.CountAsync();
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        var items = rows.Select(MapStaff).ToList();

        return Result<PagedResult<StaffNotificationDto>>.Success(new PagedResult<StaffNotificationDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<Result<int>> GetStaffUnreadCountAsync(Guid tenantId, Guid appUserId)
    {
        var now = DateTime.UtcNow;
        var count = await _dbContext.Set<Notification>().AsNoTracking()
            .CountAsync(n => n.TenantId == tenantId
                && n.AppUserId == appUserId
                && !n.IsDeleted
                && n.ReadAtUtc == null
                && (n.ExpiresAtUtc == null || n.ExpiresAtUtc > now));
        return Result<int>.Success(count);
    }

    public async Task<Result<string>> MarkStaffAsReadAsync(Guid tenantId, Guid appUserId, Guid notificationId)
    {
        var notification = await _dbContext.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.TenantId == tenantId && !n.IsDeleted);

        if (notification == null)
            return Result<string>.Failure("Notification not found / الإشعار غير موجود");

        if (notification.AppUserId != appUserId)
            return Result<string>.Failure("Forbidden: this notification belongs to another user / غير مسموح");

        if (notification.ReadAtUtc != null)
            return Result<string>.Success("Already read / تمت القراءة مسبقاً");

        notification.ReadAtUtc = DateTime.UtcNow;
        notification.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        return Result<string>.Success("Notification marked as read / تم وضع علامة مقروء");
    }

    public async Task<Result<string>> MarkAllStaffAsReadAsync(Guid tenantId, Guid appUserId)
    {
        var now = DateTime.UtcNow;
        var unread = await _dbContext.Set<Notification>()
            .Where(n => n.TenantId == tenantId
                && n.AppUserId == appUserId
                && !n.IsDeleted
                && n.ReadAtUtc == null)
            .ToListAsync();

        foreach (var n in unread)
        {
            n.ReadAtUtc = now;
            n.UpdatedAtUtc = now;
        }

        if (unread.Count > 0)
            await _dbContext.SaveChangesAsync();

        return Result<string>.Success($"Marked {unread.Count} as read / تم تعليم {unread.Count} كمقروء");
    }

    public async Task<Result<int>> PublishStaffAsync(Guid tenantId, CreateStaffNotificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Body))
            return Result<int>.Failure("Title and body are required / العنوان والنص مطلوبان");

        var dedupe = string.IsNullOrWhiteSpace(request.DedupeKey) ? null : request.DedupeKey.Trim();
        if (dedupe != null)
        {
            var exists = await _dbContext.Set<Notification>().AsNoTracking()
                .AnyAsync(n => n.TenantId == tenantId && n.ExternalMessageId == dedupe && !n.IsDeleted);
            if (exists)
                return Result<int>.Success(0);
        }

        var recipients = await ResolveRecipientsAsync(tenantId, request);
        if (recipients.Count == 0)
            return Result<int>.Success(0);

        var now = DateTime.UtcNow;
        var priority = string.IsNullOrWhiteSpace(request.Priority)
            ? StaffNotificationPriorities.Info
            : request.Priority.Trim();

        var rows = recipients.Select(uid => new Notification
        {
            TenantId = tenantId,
            AppUserId = uid,
            Channel = "in_app",
            Title = request.Title.Trim(),
            TitleAr = (request.TitleAr ?? string.Empty).Trim(),
            Body = request.Body.Trim(),
            BodyAr = (request.BodyAr ?? string.Empty).Trim(),
            Status = "sent",
            SentAtUtc = now,
            ExternalMessageId = dedupe,
            Type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim(),
            Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim(),
            Priority = priority,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            ActionUrl = request.ActionUrl,
            ExpiresAtUtc = request.ExpiresAtUtc
        }).ToList();

        await _dbContext.Set<Notification>().AddRangeAsync(rows);
        await _dbContext.SaveChangesAsync();

        try
        {
            await _realtime.NotifyCreatedAsync(tenantId, recipients);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Staff notification realtime push failed for tenant {TenantId}", tenantId);
        }

        _logger.LogInformation(
            "[Notification] Staff publish {Type}: {Count} recipients in tenant {TenantId}",
            request.Type, rows.Count, tenantId);

        return Result<int>.Success(rows.Count);
    }

    private async Task<List<Guid>> ResolveRecipientsAsync(Guid tenantId, CreateStaffNotificationRequest request)
    {
        var ids = new HashSet<Guid>();

        if (request.RecipientAppUserIds != null)
        {
            foreach (var id in request.RecipientAppUserIds.Where(x => x != Guid.Empty))
                ids.Add(id);
        }

        var staff = await _dbContext.AppUsers.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive && !u.IsDeleted)
            .Select(u => new { u.Id, u.Role })
            .ToListAsync();

        if (request.RecipientRoles != null && request.RecipientRoles.Count > 0)
        {
            var roles = new HashSet<string>(
                request.RecipientRoles.Select(RolePermissionResolver.CanonicalRole),
                StringComparer.OrdinalIgnoreCase);
            foreach (var u in staff.Where(u => roles.Contains(RolePermissionResolver.CanonicalRole(u.Role))))
                ids.Add(u.Id);
        }

        if (request.RecipientPermissions != null && request.RecipientPermissions.Count > 0)
        {
            var needed = new HashSet<string>(request.RecipientPermissions, StringComparer.Ordinal);
            var tenant = await _dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
            var overlay = RolePermissionResolver.ParseOverlay(tenant?.Settings);

            foreach (var u in staff)
            {
                var role = RolePermissionResolver.CanonicalRole(u.Role);
                if (role is "Member" or "Employee" or "")
                    continue;
                var perms = RolePermissionResolver.Resolve(new[] { role }, _permissionProvider, overlay);
                if (needed.Any(perms.Contains))
                    ids.Add(u.Id);
            }
        }

        if (ids.Count == 0)
            return new List<Guid>();

        // Only active staff in this tenant
        return await _dbContext.AppUsers.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive && !u.IsDeleted && ids.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync();
    }

    private static StaffNotificationDto MapStaff(Notification n) => new()
    {
        Id = n.Id,
        Type = n.Type,
        Category = n.Category,
        Priority = n.Priority,
        Title = n.Title,
        TitleAr = n.TitleAr,
        Body = n.Body,
        BodyAr = n.BodyAr,
        EntityType = n.EntityType,
        EntityId = n.EntityId,
        ActionUrl = n.ActionUrl,
        IsRead = n.ReadAtUtc != null,
        CreatedAtUtc = n.CreatedAtUtc,
        SentAt = n.SentAtUtc,
        SentAtUtc = n.SentAtUtc,
        ReadAtUtc = n.ReadAtUtc
    };
}
