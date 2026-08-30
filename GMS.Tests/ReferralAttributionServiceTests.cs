namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Members;
using GMS.Application.DTOs.Sales;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;
using GMS.Tests.Helpers;
using Microsoft.AspNetCore.Http;

public class ReferralAttributionServiceTests
{
    private static (GymFlowProDbContext ctx, ReferralAttributionService attrib, MemberService members, Guid tenantId, Guid referrerId, string referrerCode)
        CreateSut()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة",
            GymCode = $"T-{tenantId:N}"[..12],
            City = "Cairo",
            Address = "x",
            PhoneNumber = "01000000000",
            Email = $"{tenantId:N}@test.local",
            Settings = """{"referral_min_sale_amount_egp":100}""",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        var referrer = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "M-REF",
            FullName = "Referrer",
            FullNameAr = "محيل",
            PhoneNumber = "+201011111111",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true,
            ReferralCode = "RABCDEF1",
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.GymMembers.Add(referrer);
        ctx.SaveChanges();

        var audit = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var enc = new AesEncryptionService(new ConfigurationBuilder().Build());
        var attrib = new ReferralAttributionService(
            ctx, audit, enc, new NoOpReferralRewardService(), NullLogger<ReferralAttributionService>.Instance);
        var members = new MemberService(
            ctx, new MemberRepository(ctx), enc, new UnlimitedTierEnforcement(), attrib,
            new NoOpMemberAppActivation(),
            new ActivityEntitlementService(ctx),
            NullLogger<MemberService>.Instance);

        return (ctx, attrib, members, tenantId, referrer.Id, referrer.ReferralCode!);
    }

    [Fact]
    public async Task SelfReferral_SamePhone_Rejected()
    {
        var (ctx, attrib, _, tenantId, referrerId, code) = CreateSut();
        var result = await attrib.ResolveReferrerAsync(
            tenantId, code, null, "+201011111111");
        Assert.False(result.IsSuccess);
        Assert.Contains("Self-referral", result.Error!);

        var byId = await attrib.ResolveReferrerAsync(
            tenantId, null, referrerId, "+201011111111");
        Assert.False(byId.IsSuccess);
    }

    [Fact]
    public async Task PaidActivate_ConvertsInvitation_ByPhone_PreservesInviter()
    {
        var (ctx, attrib, members, tenantId, referrerId, _) = CreateSut();

        ctx.MemberInvitations.Add(new MemberInvitation
        {
            TenantId = tenantId,
            InvitingMemberId = referrerId,
            InvitationType = InvitationTypes.Invitation,
            GuestName = "Friend",
            GuestPhoneNumber = "+201022223333",
            Status = InvitationStatuses.New,
            QuotaPeriod = string.Empty,
            SentAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var created = await members.CreateMemberAsync(tenantId, new CreateMemberRequest
        {
            FullName = "Friend",
            FullNameAr = "صديق",
            Phone = "01022223333",
            DateOfBirth = new DateOnly(1995, 5, 5)
        });
        Assert.True(created.IsSuccess, created.Error);

        var saleId = Guid.NewGuid();
        await attrib.TryConvertOnPaidActivateAsync(
            tenantId, created.Data!.Id, saleId, amountPaid: 500m, planType: "monthly_unlimited");

        var invite = await ctx.MemberInvitations.SingleAsync();
        Assert.Equal(InvitationStatuses.Converted, invite.Status);
        Assert.Equal(saleId, invite.ConvertingSaleId);
        Assert.Equal(created.Data.Id, invite.ConvertedMemberId);
        Assert.Equal(referrerId, invite.InvitingMemberId);
        Assert.NotNull(invite.ConvertedAtUtc);

        var referrer = await ctx.GymMembers.SingleAsync(m => m.Id == referrerId);
        Assert.Equal(0, referrer.SuccessfulReferralCount);
    }

    [Fact]
    public async Task TrialOrDayPass_DoesNotConvertInvitation()
    {
        var (ctx, attrib, members, tenantId, referrerId, _) = CreateSut();
        ctx.MemberInvitations.Add(new MemberInvitation
        {
            TenantId = tenantId,
            InvitingMemberId = referrerId,
            InvitationType = InvitationTypes.Invitation,
            GuestName = "T",
            GuestPhoneNumber = "+201033334444",
            Status = InvitationStatuses.New,
            QuotaPeriod = string.Empty,
            SentAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var created = await members.CreateMemberAsync(tenantId, new CreateMemberRequest
        {
            FullName = "T",
            FullNameAr = "ت",
            Phone = "01033334444",
            DateOfBirth = new DateOnly(1995, 5, 5)
        });
        Assert.True(created.IsSuccess, created.Error);

        await attrib.TryConvertOnPaidActivateAsync(
            tenantId, created.Data!.Id, Guid.NewGuid(), 500m, "trial");
        Assert.Equal(InvitationStatuses.New, (await ctx.MemberInvitations.SingleAsync()).Status);

        await attrib.TryConvertOnPaidActivateAsync(
            tenantId, created.Data.Id, Guid.NewGuid(), 500m, "day_pass");
        Assert.Equal(InvitationStatuses.New, (await ctx.MemberInvitations.SingleAsync()).Status);
    }

    [Fact]
    public async Task InvitationConvert_DoesNotUseReferralMinSaleAmount()
    {
        var (ctx, attrib, members, tenantId, referrerId, _) = CreateSut();
        ctx.MemberInvitations.Add(new MemberInvitation
        {
            TenantId = tenantId,
            InvitingMemberId = referrerId,
            InvitationType = InvitationTypes.Invitation,
            GuestName = "Low",
            GuestPhoneNumber = "+201044445555",
            Status = InvitationStatuses.New,
            QuotaPeriod = string.Empty,
            SentAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var created = await members.CreateMemberAsync(tenantId, new CreateMemberRequest
        {
            FullName = "Low",
            FullNameAr = "ل",
            Phone = "01044445555",
            DateOfBirth = new DateOnly(1995, 5, 5)
        });
        Assert.True(created.IsSuccess, created.Error);

        await attrib.TryConvertOnPaidActivateAsync(
            tenantId, created.Data!.Id, Guid.NewGuid(), amountPaid: 50m, planType: "monthly_unlimited");

        Assert.Equal(InvitationStatuses.Converted, (await ctx.MemberInvitations.SingleAsync()).Status);
    }
}

