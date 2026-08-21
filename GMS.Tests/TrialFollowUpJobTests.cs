namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Jobs;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class TrialFollowUpJobTests
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private class RecordingWhatsAppService : IWhatsAppService
    {
        public List<(string Phone, string Template, Dictionary<string, string> Parameters)> TemplateSends { get; } = new();

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
            TemplateSends.Add((phone, templateName, parameters));
            return Task.CompletedTask;
        }
    }

    private static (GymFlowProDbContext ctx, TrialFollowUpJob job, RecordingWhatsAppService whatsApp, Guid tenantId) CreateSut()
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

        var job = new TrialFollowUpJob(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<TrialFollowUpJob>.Instance);

        return (ctx, job, whatsApp, tenantId);
    }

    private static (GymMember member, Membership membership) SeedTrialMember(
        GymFlowProDbContext ctx, Guid tenantId, MembershipPlan plan, DateOnly endDate, string phoneSuffix)
    {
        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = $"GYM-{phoneSuffix}",
            FullName = $"Trial Member {phoneSuffix}",
            FullNameAr = "عضو تجريبي",
            PhoneNumber = $"+2010000{phoneSuffix}",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25)),
            IsActive = true,
            IsTrial = true,
            TrialOutcome = "active_trial"
        };
        ctx.GymMembers.Add(member);

        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            StartDate = endDate.AddDays(-plan.DurationDays),
            EndDate = endDate,
            Status = "active"
        };
        ctx.Memberships.Add(membership);

        return (member, membership);
    }

    [Fact]
    public async Task ExecuteAsync_SendsLastDayReminder_OnlyForTrialsExpiringToday()
    {
        var (ctx, job, whatsApp, tenantId) = CreateSut();

        ctx.Tenants.Add(new Tenant
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
        });

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Free Trial",
            NameAr = "تجربة مجانية",
            PlanType = "trial",
            DurationDays = 7,
            Price = 0m
        };
        ctx.MembershipPlans.Add(plan);
        await ctx.SaveChangesAsync();

        var cairoToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));

        SeedTrialMember(ctx, tenantId, plan, cairoToday.AddDays(-1), "0001"); // expired yesterday
        var (todayMember, _) = SeedTrialMember(ctx, tenantId, plan, cairoToday, "0002"); // expires today
        SeedTrialMember(ctx, tenantId, plan, cairoToday.AddDays(1), "0003"); // expires tomorrow
        await ctx.SaveChangesAsync();

        await job.ExecuteAsync();

        var lastDayReminders = whatsApp.TemplateSends.Where(s => s.Template == "trial_last_day").ToList();

        Assert.Single(lastDayReminders);
        Assert.Equal(todayMember.PhoneNumber, lastDayReminders[0].Phone);
    }

    [Fact]
    public async Task ExecuteAsync_SendsFollowUpOfferAndMarksExpired_ForTrialsExpiredExactlyTwoDaysAgo()
    {
        var (ctx, job, whatsApp, tenantId) = CreateSut();

        ctx.Tenants.Add(new Tenant
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
        });

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Free Trial",
            NameAr = "تجربة مجانية",
            PlanType = "trial",
            DurationDays = 7,
            Price = 0m
        };
        ctx.MembershipPlans.Add(plan);
        await ctx.SaveChangesAsync();

        var cairoToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));

        var (twoDaysAgoMember, _) = SeedTrialMember(ctx, tenantId, plan, cairoToday.AddDays(-2), "0004");
        SeedTrialMember(ctx, tenantId, plan, cairoToday.AddDays(-1), "0005"); // expired only 1 day ago — not yet
        await ctx.SaveChangesAsync();

        await job.ExecuteAsync();

        var followUps = whatsApp.TemplateSends.Where(s => s.Template == "trial_followup_offer").ToList();

        Assert.Single(followUps);
        Assert.Equal(twoDaysAgoMember.PhoneNumber, followUps[0].Phone);

        var reloaded = await ctx.GymMembers.SingleAsync(m => m.Id == twoDaysAgoMember.Id);
        Assert.Equal("expired", reloaded.TrialOutcome);
    }
}
