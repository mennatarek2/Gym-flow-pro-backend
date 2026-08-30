namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

/// <summary>
/// Tenant/recipient isolation, unread counts, mark-all, and PublishStaffAsync dedupe.
/// </summary>
public class StaffNotificationIsolationTests
{
    private sealed class NoOpPush : IPushNotificationService
    {
        public Task SendToTopicAsync(string topic, string title, string body) => Task.CompletedTask;
        public Task SendToDeviceAsync(string token, string title, string body) => Task.CompletedTask;
    }

    private static (GymFlowProDbContext db, NotificationService svc, Guid tenantId, Guid ownerId, Guid receptionistId) CreateSut()
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new GymFlowProDbContext(options);
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "Gym",
            GymCode = $"T-{tenantId:N}"[..12],
            City = "Cairo",
            Address = "x",
            PhoneNumber = "01000000000",
            Email = $"{tenantId:N}@test.local",
            TimeZone = "Egypt Standard Time",
            IsActive = true,
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        var ownerId = Guid.NewGuid();
        var receptionistId = Guid.NewGuid();
        db.AppUsers.AddRange(
            new AppUser
            {
                Id = ownerId, TenantId = tenantId, UserId = Guid.NewGuid().ToString(),
                Role = "Owner", IsActive = true, Email = "o@t.com", FirstName = "O", LastName = "W",
                CreatedAtUtc = DateTime.UtcNow
            },
            new AppUser
            {
                Id = receptionistId, TenantId = tenantId, UserId = Guid.NewGuid().ToString(),
                Role = "Receptionist", IsActive = true, Email = "r@t.com", FirstName = "R", LastName = "E",
                CreatedAtUtc = DateTime.UtcNow
            },
            new AppUser
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid().ToString(),
                Role = "Trainer", IsActive = false, Email = "x@t.com", FirstName = "X", LastName = "Y",
                CreatedAtUtc = DateTime.UtcNow
            });
        db.SaveChanges();

        // Signature: (db, push, logger, permissionProvider?, realtime?)
        var svc = new NotificationService(
            db,
            new NoOpPush(),
            NullLogger<NotificationService>.Instance);

        return (db, svc, tenantId, ownerId, receptionistId);
    }

    [Fact]
    public async Task PublishStaff_ResolvesRoles_AndSkipsInactive()
    {
        var (db, svc, tenantId, ownerId, receptionistId) = CreateSut();

        var result = await svc.PublishStaffAsync(tenantId, new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.CashVariance,
            Category = StaffNotificationCategories.Shifts,
            Priority = StaffNotificationPriorities.Critical,
            Title = "Variance",
            TitleAr = "Diff",
            Body = "Cash off",
            BodyAr = "Cash",
            RecipientRoles = new[] { "Owner", "Receptionist", "Trainer" }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data);
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.TenantId == tenantId && n.AppUserId != null));
        Assert.Contains(await db.Notifications.ToListAsync(), n => n.AppUserId == ownerId);
        Assert.Contains(await db.Notifications.ToListAsync(), n => n.AppUserId == receptionistId);
    }

    [Fact]
    public async Task PublishStaff_Dedupe_SkipsSecondPublish()
    {
        var (db, svc, tenantId, _, _) = CreateSut();
        var req = new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.LowStock,
            Category = StaffNotificationCategories.Inventory,
            Priority = StaffNotificationPriorities.ActionRequired,
            Title = "Low",
            TitleAr = "Low",
            Body = "SKU",
            BodyAr = "SKU",
            DedupeKey = "inv-low-test:1",
            RecipientRoles = new[] { "Owner" }
        };

        Assert.Equal(1, (await svc.PublishStaffAsync(tenantId, req)).Data);
        Assert.Equal(0, (await svc.PublishStaffAsync(tenantId, req)).Data);
        Assert.Equal(1, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task MarkStaffAsRead_EnforcesRecipientIsolation()
    {
        var (db, svc, tenantId, ownerId, receptionistId) = CreateSut();
        await svc.PublishStaffAsync(tenantId, new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.EmployeeActivated,
            Category = StaffNotificationCategories.Staff,
            Priority = StaffNotificationPriorities.Info,
            Title = "Hi",
            TitleAr = "Hi",
            Body = "Body",
            BodyAr = "Body",
            RecipientAppUserIds = new[] { ownerId }
        });

        var note = await db.Notifications.SingleAsync(n => n.AppUserId == ownerId);
        var forbidden = await svc.MarkStaffAsReadAsync(tenantId, receptionistId, note.Id);
        Assert.False(forbidden.IsSuccess);
        Assert.Contains("Forbidden", forbidden.Error!, StringComparison.OrdinalIgnoreCase);

        var ok = await svc.MarkStaffAsReadAsync(tenantId, ownerId, note.Id);
        Assert.True(ok.IsSuccess);
        Assert.NotNull((await db.Notifications.SingleAsync(n => n.Id == note.Id)).ReadAtUtc);
    }

    [Fact]
    public async Task GetStaffNotifications_IsTenantAndRecipientScoped_UnreadAndMarkAll()
    {
        var (db, svc, tenantId, ownerId, _) = CreateSut();
        var otherTenant = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = otherTenant,
            Name = "Other",
            NameAr = "Other",
            GymCode = $"O-{otherTenant:N}"[..12],
            City = "Giza",
            Address = "y",
            PhoneNumber = "01000000001",
            Email = $"{otherTenant:N}@test.local",
            TimeZone = "UTC",
            IsActive = true,
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        var otherUser = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = otherUser, TenantId = otherTenant, UserId = Guid.NewGuid().ToString(),
            Role = "Owner", IsActive = true, Email = "a@b.com", FirstName = "A", LastName = "B",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await svc.PublishStaffAsync(tenantId, new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.SecurityAlert,
            Category = StaffNotificationCategories.Security,
            Priority = StaffNotificationPriorities.Critical,
            Title = "Mine",
            TitleAr = "Mine",
            Body = "x",
            BodyAr = "x",
            RecipientAppUserIds = new[] { ownerId }
        });
        await svc.PublishStaffAsync(otherTenant, new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.SecurityAlert,
            Category = StaffNotificationCategories.Security,
            Priority = StaffNotificationPriorities.Critical,
            Title = "Other",
            TitleAr = "Other",
            Body = "y",
            BodyAr = "y",
            RecipientAppUserIds = new[] { otherUser }
        });

        var mine = await svc.GetStaffNotificationsAsync(tenantId, ownerId, 1, 20);
        Assert.True(mine.IsSuccess);
        Assert.Single(mine.Data!.Items);
        Assert.Equal("Mine", mine.Data.Items[0].Title);

        var count = await svc.GetStaffUnreadCountAsync(tenantId, ownerId);
        Assert.Equal(1, count.Data);

        await svc.MarkAllStaffAsReadAsync(tenantId, ownerId);
        count = await svc.GetStaffUnreadCountAsync(tenantId, ownerId);
        Assert.Equal(0, count.Data);

        var otherCount = await svc.GetStaffUnreadCountAsync(otherTenant, otherUser);
        Assert.Equal(1, otherCount.Data);
    }
}
