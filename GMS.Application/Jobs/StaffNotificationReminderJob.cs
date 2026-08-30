namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Hourly staff reminders: trial ending (3d), membership expiring (7d), membership expired (today),
/// and open call-sheet follow-ups due/overdue. Idempotent via ExternalMessageId dedupe keys.
/// </summary>
public class StaffNotificationReminderJob
{
    private static readonly TimeZoneInfo CairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
    private static readonly string[] DeskRoles = { "Owner", "Manager", "Receptionist" };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaffNotificationReminderJob> _logger;

    public StaffNotificationReminderJob(
        IServiceScopeFactory scopeFactory,
        ILogger<StaffNotificationReminderJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
    public async Task ExecuteAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var cairoToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTz));
        var tenants = await db.Tenants.AsNoTracking()
            .Where(t => t.IsActive && !t.IsDeleted)
            .Select(t => new { t.Id, t.Name, t.TimeZone })
            .ToListAsync();

        foreach (var tenant in tenants)
        {
            try
            {
                tenantContext.SetTenant(tenant.Id, tenant.Name, tenant.TimeZone);
                await RunForTenantAsync(db, notifications, tenant.Id, cairoToday);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StaffNotificationReminderJob failed for tenant {TenantId}", tenant.Id);
            }
        }
    }

    private static async Task RunForTenantAsync(
        GymFlowProDbContext db,
        INotificationService notifications,
        Guid tenantId,
        DateOnly cairoToday)
    {
        var trialTarget = cairoToday.AddDays(3);
        var expiringTarget = cairoToday.AddDays(7);
        var dayKey = cairoToday.ToString("yyyyMMdd");

        var memberships = await db.Set<Core.Entities.Membership>().AsNoTracking()
            .Include(m => m.Member)
            .Include(m => m.Plan)
            .Where(m => m.TenantId == tenantId
                && !m.IsDeleted
                && m.Status == "active"
                && (m.EndDate == trialTarget || m.EndDate == expiringTarget || m.EndDate == cairoToday))
            .ToListAsync();

        foreach (var m in memberships)
        {
            var memberName = m.Member?.FullName ?? "Member";
            var isTrial = m.Plan?.PlanType == "trial" || (m.Member?.IsTrial ?? false);
            var actionUrl = $"/dashboard/members/{m.MemberId}/";

            if (m.EndDate == trialTarget && isTrial)
            {
                await notifications.PublishStaffAsync(tenantId, new CreateStaffNotificationRequest
                {
                    Type = StaffNotificationTypes.TrialEnding,
                    Category = StaffNotificationCategories.Memberships,
                    Priority = StaffNotificationPriorities.ActionRequired,
                    Title = "Trial ending soon",
                    TitleAr = "التجربة تنتهي قريباً",
                    Body = $"{memberName}'s trial ends in 3 days.",
                    BodyAr = $"تجربة {memberName} تنتهي خلال 3 أيام.",
                    EntityType = "Membership",
                    EntityId = m.Id,
                    ActionUrl = actionUrl,
                    DedupeKey = $"trial-ending:{dayKey}:{m.Id:N}",
                    RecipientRoles = DeskRoles
                });
            }
            else if (m.EndDate == expiringTarget && !isTrial)
            {
                await notifications.PublishStaffAsync(tenantId, new CreateStaffNotificationRequest
                {
                    Type = StaffNotificationTypes.MembershipExpiring,
                    Category = StaffNotificationCategories.Memberships,
                    Priority = StaffNotificationPriorities.ActionRequired,
                    Title = "Membership expiring soon",
                    TitleAr = "العضوية تنتهي قريباً",
                    Body = $"{memberName}'s membership ends in 7 days.",
                    BodyAr = $"عضوية {memberName} تنتهي خلال 7 أيام.",
                    EntityType = "Membership",
                    EntityId = m.Id,
                    ActionUrl = actionUrl,
                    DedupeKey = $"membership-expiring:{dayKey}:{m.Id:N}",
                    RecipientRoles = DeskRoles
                });
            }
            else if (m.EndDate == cairoToday)
            {
                await notifications.PublishStaffAsync(tenantId, new CreateStaffNotificationRequest
                {
                    Type = StaffNotificationTypes.MembershipExpired,
                    Category = StaffNotificationCategories.Memberships,
                    Priority = StaffNotificationPriorities.Critical,
                    Title = "Membership expired",
                    TitleAr = "انتهت العضوية",
                    Body = $"{memberName}'s membership expired today.",
                    BodyAr = $"انتهت عضوية {memberName} اليوم.",
                    EntityType = "Membership",
                    EntityId = m.Id,
                    ActionUrl = actionUrl,
                    DedupeKey = $"membership-expired:{dayKey}:{m.Id:N}",
                    RecipientRoles = DeskRoles
                });
            }
        }

        await PublishFollowUpRemindersAsync(db, notifications, tenantId, cairoToday, dayKey);
    }

    private static async Task PublishFollowUpRemindersAsync(
        GymFlowProDbContext db,
        INotificationService notifications,
        Guid tenantId,
        DateOnly cairoToday,
        string dayKey)
    {
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(cairoToday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            CairoTz);
        var dayEndUtc = dayStartUtc.AddDays(1);
        var openStatuses = new[] { "pending", "in_progress", "no_answer" };

        var followUps = await db.Set<Core.Entities.MemberFollowUp>().AsNoTracking()
            .Include(f => f.Member)
            .Where(f => f.TenantId == tenantId
                && !f.IsDeleted
                && openStatuses.Contains(f.Status)
                && f.DueAtUtc < dayEndUtc)
            .ToListAsync();

        foreach (var f in followUps)
        {
            var memberName = f.Member?.FullName ?? "Member";
            var isOverdue = f.DueAtUtc < dayStartUtc;
            await notifications.PublishStaffAsync(tenantId, new CreateStaffNotificationRequest
            {
                Type = isOverdue ? StaffNotificationTypes.FollowUpOverdue : StaffNotificationTypes.FollowUpDue,
                Category = StaffNotificationCategories.Leads,
                Priority = isOverdue
                    ? StaffNotificationPriorities.Critical
                    : StaffNotificationPriorities.ActionRequired,
                Title = isOverdue ? "Follow-up overdue" : "Follow-up due today",
                TitleAr = isOverdue ? "متابعة متأخرة" : "متابعة مستحقة اليوم",
                Body = $"{memberName}: {f.Reason}",
                BodyAr = $"{memberName}: {f.Reason}",
                EntityType = "MemberFollowUp",
                EntityId = f.Id,
                ActionUrl = "/dashboard/call-sheet/",
                DedupeKey = isOverdue
                    ? $"followup-overdue:{dayKey}:{f.Id:N}"
                    : $"followup-due:{dayKey}:{f.Id:N}",
                RecipientRoles = DeskRoles,
                RecipientAppUserIds = f.AssignedToUserId.HasValue
                    ? new[] { f.AssignedToUserId.Value }
                    : null
            });
        }
    }
}
