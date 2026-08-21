namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class ReferralRewardServiceTests
{
    private sealed class CapturingWhatsApp : GMS.Core.Interfaces.IWhatsAppService
    {
        public List<string> Templates { get; } = new();
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
            Templates.Add(templateName);
            return Task.CompletedTask;
        }
    }

    private static async Task<(GymFlowProDbContext ctx, ReferralRewardService svc, CapturingWhatsApp wa,
        Guid tenantId, Guid saleId, Guid invitationId, Guid referrerId, Guid refereeId, Guid appUserId)> SeedAsync(
        int holdDays = 0,
        decimal planPrice = 500m,
        string? planRewardType = "credit",
        decimal? planRewardValue = 50m,
        string planType = "monthly_unlimited",
        decimal familyMultiplier = 1.5m)
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Gym", NameAr = "ص", GymCode = $"G{tenantId:N}"[..8],
            City = "Cairo", Address = "x", PhoneNumber = "01000000000",
            Email = "g@t.local",
            Settings =
                $"{{\"referral_hold_days\":{holdDays},\"referral_min_sale_amount_egp\":0," +
                $"\"referral_family_reward_multiplier\":{familyMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}",
            SubscriptionStartDate = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
        });

        var appUser = new AppUser
        {
            TenantId = tenantId, UserId = Guid.NewGuid().ToString(),
            FirstName = "Owner", LastName = "O", Email = "o@t.local", Role = "Owner",
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(appUser);

        var referrer = new GymMember
        {
            TenantId = tenantId, MemberNumber = "R1", FullName = "Referrer", FullNameAr = "م",
            PhoneNumber = "+201011111111", DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true, ReferralCode = "RREF0001", SuccessfulReferralCount = 1,
            AppUserId = appUser.Id, CreatedAtUtc = DateTime.UtcNow
        };
        var referee = new GymMember
        {
            TenantId = tenantId, MemberNumber = "R2", FullName = "Friend", FullNameAr = "ص",
            PhoneNumber = "+201022222222", DateOfBirth = new DateOnly(1992, 1, 1),
            IsActive = true, ReferralCode = "RREF0002", CreatedAtUtc = DateTime.UtcNow
        };
        ctx.GymMembers.AddRange(referrer, referee);

        var plan = new MembershipPlan
        {
            TenantId = tenantId, Name = "Month", NameAr = "ش", PlanType = planType,
            DurationDays = 30, Price = planPrice, IsActive = true,
            ReferralRewardType = planRewardType, ReferralRewardValue = planRewardValue,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.MembershipPlans.Add(plan);

        var today = MembershipOperational.TodayCairo();
        var membership = new Membership
        {
            TenantId = tenantId, MemberId = referee.Id, PlanId = plan.Id,
            StartDate = today, EndDate = today.AddDays(30), Status = "active",
            AmountPaid = planPrice, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Memberships.Add(membership);

        // Referrer also needs active membership for free_days dual grant
        ctx.Memberships.Add(new Membership
        {
            TenantId = tenantId, MemberId = referrer.Id, PlanId = plan.Id,
            StartDate = today, EndDate = today.AddDays(20), Status = "active",
            AmountPaid = planPrice, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        var sale = new Sale
        {
            TenantId = tenantId, MemberId = referee.Id, SoldByUserId = appUser.Id,
            Subtotal = planPrice, Total = planPrice, AmountDue = 0, Status = "completed"
        };
        ctx.Sales.Add(sale);
        ctx.SaleLines.Add(new SaleLine
        {
            TenantId = tenantId, SaleId = sale.Id, LineType = "membership",
            ReferenceId = membership.Id, Description = plan.Name, DescriptionAr = plan.NameAr,
            Qty = 1, UnitPrice = planPrice, LineTotal = planPrice
        });

        var invite = new MemberInvitation
        {
            TenantId = tenantId, InvitingMemberId = referrer.Id, ConvertedMemberId = referee.Id,
            InvitationType = InvitationTypes.Referral, GuestName = referee.FullName,
            GuestPhoneNumber = referee.PhoneNumber, Status = "converted",
            ReferralCodeUsed = referrer.ReferralCode, ConvertingSaleId = sale.Id,
            ConvertedAtUtc = DateTime.UtcNow, SentAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
        };
        ctx.MemberInvitations.Add(invite);
        await ctx.SaveChangesAsync();

        var wa = new CapturingWhatsApp();
        var audit = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var svc = new ReferralRewardService(ctx, audit, wa, NullLogger<ReferralRewardService>.Instance);

        return (ctx, svc, wa, tenantId, sale.Id, invite.Id, referrer.Id, referee.Id, appUser.Id);
    }

    [Fact]
    public async Task HoldThenGrant_CreatesDualCredits()
    {
        var (ctx, svc, wa, tenantId, saleId, inviteId, referrerId, refereeId, _) =
            await SeedAsync(holdDays: 0);

        await svc.CreateHoldsForConvertedInvitationAsync(tenantId, inviteId, saleId, 500m);
        Assert.Equal(2, await ctx.ReferralRewards.CountAsync());
        Assert.All(await ctx.ReferralRewards.ToListAsync(),
            r => Assert.Equal(ReferralRewardStatuses.PendingHold, r.Status));

        var granted = await svc.ProcessDueHoldsAsync();
        Assert.Equal(2, granted);

        var rows = await ctx.ReferralRewards.ToListAsync();
        Assert.All(rows, r => Assert.Equal(ReferralRewardStatuses.Granted, r.Status));

        var credits = await ctx.MemberCredits
            .Where(c => c.EntryType == MemberCreditEntryTypes.ReferralReward && c.Amount > 0)
            .ToListAsync();
        Assert.Equal(2, credits.Count);
        Assert.Contains(credits, c => c.MemberId == referrerId && c.Amount == 50m);
        Assert.Contains(credits, c => c.MemberId == refereeId && c.Amount == 50m);
        Assert.Contains(wa.Templates, t => t == "referral_reward_granted");
    }

    [Fact]
    public async Task RefundInHold_Forfeits()
    {
        var (ctx, svc, _, tenantId, saleId, inviteId, _, _, _) =
            await SeedAsync(holdDays: 14);

        await svc.CreateHoldsForConvertedInvitationAsync(tenantId, inviteId, saleId, 500m);
        await svc.HandleConvertingSaleRefundedAsync(tenantId, saleId);

        Assert.All(await ctx.ReferralRewards.ToListAsync(),
            r => Assert.Equal(ReferralRewardStatuses.Forfeited, r.Status));
        Assert.Empty(await ctx.MemberCredits.ToListAsync());
    }

    [Fact]
    public async Task RefundAfterGrant_ReversesCredits()
    {
        var (ctx, svc, _, tenantId, saleId, inviteId, referrerId, _, _) =
            await SeedAsync(holdDays: 0);

        await svc.CreateHoldsForConvertedInvitationAsync(tenantId, inviteId, saleId, 500m);
        await svc.ProcessDueHoldsAsync();

        await svc.HandleConvertingSaleRefundedAsync(tenantId, saleId);

        Assert.All(await ctx.ReferralRewards.ToListAsync(),
            r => Assert.Equal(ReferralRewardStatuses.Reversed, r.Status));

        var net = await ctx.MemberCredits
            .Where(c => c.MemberId == referrerId && c.EntryType == MemberCreditEntryTypes.ReferralReward)
            .SumAsync(c => c.Amount);
        Assert.Equal(0m, net);
    }

    [Fact]
    public async Task FreeDays_GrantExtendsEndDate()
    {
        var (ctx, svc, _, tenantId, saleId, inviteId, referrerId, _, _) =
            await SeedAsync(holdDays: 0, planPrice: 2000m, planRewardType: "free_days", planRewardValue: 7m);

        var before = await ctx.Memberships.Where(m => m.MemberId == referrerId).Select(m => m.EndDate).FirstAsync();

        await svc.CreateHoldsForConvertedInvitationAsync(tenantId, inviteId, saleId, 2000m);
        await svc.ProcessDueHoldsAsync();

        var after = await ctx.Memberships.Where(m => m.MemberId == referrerId).Select(m => m.EndDate).FirstAsync();
        Assert.Equal(before.AddDays(7), after);
        Assert.All(await ctx.ReferralRewards.ToListAsync(),
            r => Assert.Equal(ReferralRewardStatuses.Granted, r.Status));
    }

    [Fact]
    public async Task FamilyPlan_AppliesMultiplierPremium()
    {
        var (ctx, svc, _, tenantId, saleId, inviteId, _, _, _) =
            await SeedAsync(
                holdDays: 14,
                planPrice: 800m,
                planRewardType: "credit",
                planRewardValue: 50m,
                planType: "family",
                familyMultiplier: 1.5m);

        await svc.CreateHoldsForConvertedInvitationAsync(tenantId, inviteId, saleId, 800m);

        var rows = await ctx.ReferralRewards.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.True(r.IsFamily);
            Assert.Equal(75m, r.RewardValue); // 50 × 1.5
            Assert.Equal(ReferralRewardStatuses.PendingHold, r.Status);
        });
    }

    [Fact]
    public async Task NonFamilyPlan_NoPremiumLabelOrMultiplier()
    {
        var (ctx, svc, _, tenantId, saleId, inviteId, _, _, _) =
            await SeedAsync(holdDays: 14, planRewardType: "credit", planRewardValue: 50m, planType: "monthly_unlimited");

        await svc.CreateHoldsForConvertedInvitationAsync(tenantId, inviteId, saleId, 500m);

        Assert.All(await ctx.ReferralRewards.ToListAsync(), r =>
        {
            Assert.False(r.IsFamily);
            Assert.Equal(50m, r.RewardValue);
        });
    }
}
