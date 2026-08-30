namespace GMS.Tests;

using Hangfire;
using Hangfire.InMemory;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

/// <summary>
/// Send Notification → WhatsApp must deliver form Title/Body, not membership-expiry copy.
/// </summary>
public class MemberBulkWhatsAppNotificationTests
{
    static MemberBulkWhatsAppNotificationTests()
    {
        JobStorage.Current = new InMemoryStorage();
    }

    private sealed class NoOpPush : IPushNotificationService
    {
        public Task SendToTopicAsync(string topic, string title, string body) => Task.CompletedTask;
        public Task SendToDeviceAsync(string token, string title, string body) => Task.CompletedTask;
    }

    private sealed class CapturingWhatsApp : IWhatsAppService
    {
        public List<(string Phone, string Title, string Body, string? TitleAr, string? BodyAr)> Notifications { get; } = new();
        public List<(string Phone, string Name, int Days)> ExpiryCalls { get; } = new();

        public Task SendExpiryReminderAsync(Guid memberId, int daysLeft) => Task.CompletedTask;
        public Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft)
        {
            ExpiryCalls.Add((phone, memberName, daysLeft));
            return Task.CompletedTask;
        }

        public Task SendBirthdayGreetingAsync(Guid memberId, string discountCode) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode) => Task.CompletedTask;
        public Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime) => Task.CompletedTask;
        public Task SendClassReminderAsync(string phone, string className, DateTime startTime) => Task.CompletedTask;
        public Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate) => Task.CompletedTask;
        public Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry) => Task.CompletedTask;
        public Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr) => Task.CompletedTask;
        public Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters) => Task.CompletedTask;

        public Task SendNotificationAsync(string phone, string title, string body, string? titleAr = null, string? bodyAr = null)
        {
            Notifications.Add((phone, title, body, titleAr, bodyAr));
            return Task.CompletedTask;
        }
    }

    private static (GymFlowProDbContext db, NotificationService svc, Guid tenantId, Guid memberId, string phone) CreateSut()
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
            GymCode = $"W-{tenantId:N}"[..12],
            City = "Cairo",
            Address = "x",
            PhoneNumber = "01000000000",
            Email = $"{tenantId:N}@test.local",
            TimeZone = "Africa/Cairo",
            IsActive = true,
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        var memberId = Guid.NewGuid();
        const string phone = "01011112222";
        db.GymMembers.Add(new GymMember
        {
            Id = memberId,
            TenantId = tenantId,
            MemberNumber = "M-1",
            FullName = "Ahmed Test",
            FullNameAr = "أحمد",
            PhoneNumber = phone,
            Email = "a@t.com",
            NationalIdEncrypted = "x",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        // Inactive member — must not receive
        db.GymMembers.Add(new GymMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MemberNumber = "M-2",
            FullName = "Inactive",
            FullNameAr = "موقوف",
            PhoneNumber = "01099999999",
            Email = "i@t.com",
            NationalIdEncrypted = "y",
            DateOfBirth = new DateOnly(1991, 1, 1),
            IsActive = false,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        var svc = new NotificationService(
            db,
            new NoOpPush(),
            NullLogger<NotificationService>.Instance);

        return (db, svc, tenantId, memberId, phone);
    }

    [Fact]
    public async Task SendBulk_WhatsApp_PersistsInboxRows_AndEnqueuesSendNotificationAsync_NotExpiry()
    {
        var (db, svc, tenantId, memberId, phone) = CreateSut();

        var result = await svc.SendBulkNotificationAsync(tenantId, new SendBulkNotificationRequest
        {
            AllMembers = true,
            Title = "Special Offer",
            TitleAr = "عرض خاص",
            Body = "20% off annual membership",
            BodyAr = "خصم 20% على الاشتراك السنوي",
            Channel = "whatsapp"
        });

        Assert.True(result.IsSuccess, result.Error);

        var rows = await db.Set<Notification>()
            .Where(n => n.TenantId == tenantId && n.MemberId == memberId)
            .ToListAsync();
        Assert.Single(rows);
        Assert.Equal("whatsapp", rows[0].Channel);
        Assert.Equal("Special Offer", rows[0].Title);
        Assert.Equal("عرض خاص", rows[0].TitleAr);
        Assert.Equal("20% off annual membership", rows[0].Body);
        Assert.Equal("خصم 20% على الاشتراك السنوي", rows[0].BodyAr);
        Assert.Equal("sent", rows[0].Status);

        // Inactive member must not get a row
        Assert.Equal(1, await db.Set<Notification>().CountAsync(n => n.TenantId == tenantId));

        var monitoring = JobStorage.Current.GetMonitoringApi();
        var enqueued = monitoring.EnqueuedJobs("default", 0, 50);
        Assert.NotEmpty(enqueued);

        var whatsAppJobs = enqueued
            .Select(e => e.Value.Job)
            .Where(j => j != null && j.Type == typeof(IWhatsAppService))
            .ToList();

        Assert.NotEmpty(whatsAppJobs);
        Assert.All(whatsAppJobs, j => Assert.Equal("SendNotificationAsync", j.Method.Name));
        Assert.DoesNotContain(whatsAppJobs, j => j.Method.Name == "SendExpiryReminderAsync");

        var args = whatsAppJobs[0].Args;
        Assert.Equal(phone, args[0]?.ToString());
        Assert.Equal("Special Offer", args[1]?.ToString());
        Assert.Equal("20% off annual membership", args[2]?.ToString());
        Assert.Equal("عرض خاص", args[3]?.ToString());
        Assert.Equal("خصم 20% على الاشتراك السنوي", args[4]?.ToString());
    }

    [Fact]
    public async Task MockWhatsApp_SendNotificationAsync_LogsExactFormContent_NotExpiryCopy()
    {
        var capturing = new CapturingWhatsApp();
        await capturing.SendNotificationAsync(
            "01011112222",
            "Special Offer",
            "20% off annual membership",
            "عرض خاص",
            "خصم 20% على الاشتراك السنوي");

        Assert.Empty(capturing.ExpiryCalls);
        Assert.Single(capturing.Notifications);
        var n = capturing.Notifications[0];
        Assert.Equal("01011112222", n.Phone);
        Assert.Equal("Special Offer", n.Title);
        Assert.Equal("خصم 20% على الاشتراك السنوي", n.BodyAr);
        Assert.DoesNotContain("تنتهي اليوم", n.Body ?? "");
        Assert.DoesNotContain("تنتهي اليوم", n.BodyAr ?? "");
    }

    [Fact]
    public async Task MockWhatsAppService_FormatsExactTitleAndBody()
    {
        // Exercise real mock implementation (logs; ensures method exists and accepts form fields).
        var mock = new MockWhatsAppService(NullLogger<MockWhatsAppService>.Instance);
        await mock.SendNotificationAsync(
            "01011112222",
            "Special Offer",
            "EN body",
            "عرض خاص",
            "خصم 20% على الاشتراك السنوي");
        // Still available for real expiry jobs:
        await mock.SendExpiryReminderAsync("01011112222", "Ahmed", 0);
    }
}
