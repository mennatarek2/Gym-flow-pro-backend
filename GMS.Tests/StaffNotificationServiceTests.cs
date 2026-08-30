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

public class StaffNotificationServiceTests
{
    private sealed class NoOpPush : IPushNotificationService
    {
        public Task SendToTopicAsync(string topic, string title, string body) => Task.CompletedTask;
        public Task SendToDeviceAsync(string token, string title, string body) => Task.CompletedTask;
    }

    private static (GymFlowProDbContext db, NotificationService svc, TenantContext tenantContext, Guid tenantId, Guid ownerId, Guid receptionistId) CreateSut()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        var db = new GymFlowProDbContext(options, tenantContext);

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة",
            GymCode = $"T-{tenantId:N}"[..12],
            City = "Cairo",
            Address = "x",
            PhoneNumber = "01000000000",
            Email = $"{tenantId:N}@test.local",
            TimeZone = "Africa/Cairo",
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
                PhoneNumber = "01000000001", CreatedAtUtc = DateTime.UtcNow
            },
            new AppUser
            {
                Id = receptionistId, TenantId = tenantId, UserId = Guid.NewGuid().ToString(),
                Role = "Receptionist", IsActive = true, Email = "r@t.com", FirstName = "R", LastName = "E",
                PhoneNumber = "01000000002", CreatedAtUtc = DateTime.UtcNow
            },
            new AppUser
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid().ToString(),
                Role = "Trainer", IsActive = false, Email = "x@t.com", FirstName = "X", LastName = "Y",
                PhoneNumber = "01000000003", CreatedAtUtc = DateTime.UtcNow
            });
        db.SaveChanges();

        var svc = new NotificationService(
            db,
            new NoOpPush(),
            NullLogger<NotificationService>.Instance,
            new DefaultPermissionProvider(),
            new NullStaffNotificationRealtimeNotifier());

        return (db, svc, tenantContext, tenantId, ownerId, receptionistId);
    }

    [Fact]
    public async Task PublishStaff_ResolvesRoles_AndSkipsInactive()
    {
        var (db, svc, _, tenantId, ownerId, receptionistId) = CreateSut();

        var result = await svc.PublishStaffAsync(tenantId, new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.CashVariance,
            Category = StaffNotificationCategories.Shifts,
            Priority = StaffNotificationPriorities.Critical,
            Title = "Variance",
            TitleAr = "فرق",
            Body = "Cash off",
            BodyAr = "فرق نقدي",
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
        var (db, svc, _, tenantId, _, _) = CreateSut();
        var req = new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.LowStock,
            Category = StaffNotificationCategories.Inventory,
            Priority = StaffNotificationPriorities.ActionRequired,
            Title = "Low",
            TitleAr = "منخفض",
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
        var (db, svc, _, tenantId, ownerId, receptionistId) = CreateSut();
        await svc.PublishStaffAsync(tenantId, new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.EmployeeActivated,
            Category = StaffNotificationCategories.Staff,
            Priority = StaffNotificationPriorities.Info,
            Title = "Hi",
            TitleAr = "مرحبا",
            Body = "Body",
            BodyAr = "نص",
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
    public async Task GetStaffNotifications_IsTenantAndRecipientScoped()
    {
        var (db, svc, tenantContext, tenantId, ownerId, _) = CreateSut();
        var otherTenant = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = otherTenant,
            Name = "Other",
            NameAr = "أخرى",
            GymCode = $"O-{otherTenant:N}"[..12],
            City = "Cairo",
            Address = "y",
            PhoneNumber = "01000000009",
            Email = $"{otherTenant:N}@other.local",
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
            PhoneNumber = "01000000008", CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await svc.PublishStaffAsync(tenantId, new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.SecurityAlert,
            Category = StaffNotificationCategories.Security,
            Priority = StaffNotificationPriorities.Critical,
            Title = "Mine",
            TitleAr = "لي",
            Body = "x",
            BodyAr = "x",
            RecipientAppUserIds = new[] { ownerId }
        });

        // Switch tenant filter so the other-tenant user is visible to recipient resolution.
        tenantContext.SetTenant(otherTenant, "Other", "UTC");
        await svc.PublishStaffAsync(otherTenant, new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.SecurityAlert,
            Category = StaffNotificationCategories.Security,
            Priority = StaffNotificationPriorities.Critical,
            Title = "Other",
            TitleAr = "آخر",
            Body = "y",
            BodyAr = "y",
            RecipientAppUserIds = new[] { otherUser }
        });
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");

        var mine = await svc.GetStaffNotificationsAsync(tenantId, ownerId, 1, 20);
        Assert.True(mine.IsSuccess);
        Assert.Single(mine.Data!.Items);
        Assert.Equal("Mine", mine.Data.Items[0].Title);

        var count = await svc.GetStaffUnreadCountAsync(tenantId, ownerId);
        Assert.Equal(1, count.Data);

        await svc.MarkAllStaffAsReadAsync(tenantId, ownerId);
        count = await svc.GetStaffUnreadCountAsync(tenantId, ownerId);
        Assert.Equal(0, count.Data);

        tenantContext.SetTenant(otherTenant, "Other", "UTC");
        var otherCount = await svc.GetStaffUnreadCountAsync(otherTenant, otherUser);
        Assert.Equal(1, otherCount.Data);
    }
}
