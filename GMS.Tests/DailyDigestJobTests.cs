namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Jobs;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class DailyDigestJobTests
{
    private class RecordingWhatsAppService : IWhatsAppService
    {
        public List<(string Phone, string Template)> TemplateSends { get; } = new();

        public Task SendExpiryReminderAsync(Guid memberId, int daysLeft) => Task.CompletedTask;
        public Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(Guid memberId, string discountCode) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode) => Task.CompletedTask;
        public Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime) => Task.CompletedTask;
        public Task SendClassReminderAsync(string phone, string className, DateTime startTime) => Task.CompletedTask;
        public Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate) => Task.CompletedTask;
        public Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry) => Task.CompletedTask;
        public Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr) => Task.CompletedTask;

        public Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters)
        {
            TemplateSends.Add((phone, templateName));
            return Task.CompletedTask;
        }
    }

    private static (GymFlowProDbContext ctx, DailyDigestJob job, RecordingWhatsAppService whatsApp, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var whatsApp = new RecordingWhatsAppService();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddSingleton<IWhatsAppService>(whatsApp);
        var provider = services.BuildServiceProvider();

        var job = new DailyDigestJob(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DailyDigestJob>.Instance);

        return (ctx, job, whatsApp, tenantId);
    }

    private static Tenant SeedTenant(GymFlowProDbContext ctx, Guid tenantId)
    {
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة اختبار",
            GymCode = $"GYM-{tenantId:N}".Substring(0, 13),
            City = "Cairo",
            Address = "Test Address",
            PhoneNumber = "0100000000",
            Email = $"{tenantId}@test.local",
            IsActive = true,
            SubscriptionStartDate = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        return tenant;
    }

    private static void SeedOwner(GymFlowProDbContext ctx, Guid tenantId)
    {
        ctx.AppUsers.Add(new AppUser
        {
            TenantId = tenantId,
            UserId = Guid.NewGuid().ToString(),
            FirstName = "Ahmed",
            LastName = "Owner",
            Email = $"owner-{Guid.NewGuid()}@test.local",
            PhoneNumber = "+201000000000",
            Role = "Owner",
            IsActive = true
        });
    }

    [Fact]
    public async Task ExecuteAsync_ZeroExpiringAndZeroDebtors_DoesNotSendWhatsApp()
    {
        var (ctx, job, whatsApp, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        SeedOwner(ctx, tenantId);
        await ctx.SaveChangesAsync();

        await job.ExecuteAsync();

        Assert.Empty(whatsApp.TemplateSends);
    }

    [Fact]
    public async Task ExecuteAsync_OneDebtor_SendsDigestToOwner()
    {
        var (ctx, job, whatsApp, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        SeedOwner(ctx, tenantId);

        ctx.Sales.Add(new Sale
        {
            TenantId = tenantId,
            MemberId = Guid.NewGuid(),
            SoldByUserId = Guid.NewGuid(),
            Subtotal = 300m,
            Total = 300m,
            AmountDue = 100m,
            Status = "partially_paid"
        });
        await ctx.SaveChangesAsync();

        await job.ExecuteAsync();

        var digestSends = whatsApp.TemplateSends.Where(s => s.Template == "daily_digest").ToList();
        Assert.Single(digestSends);
        Assert.Equal("+201000000000", digestSends[0].Phone);
    }
}
