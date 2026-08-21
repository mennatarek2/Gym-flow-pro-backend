namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Invitation;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class InvitationServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private sealed class FakeEncryption : IEncryptionService
    {
        public string Encrypt(string plainText) => "enc:" + plainText;
        public string Decrypt(string cipherText)
            => cipherText.StartsWith("enc:", StringComparison.Ordinal) ? cipherText[4..] : cipherText;
    }

    private static (GymFlowProDbContext ctx, InvitationService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        var svc = new InvitationService(
            ctx, new FakeEncryption(), new NoOpAudit(), NullLogger<InvitationService>.Instance);
        return (ctx, svc, tenantId);
    }

    private static (Guid identityUserId, Guid gymMemberId, Guid membershipId) SeedLinkedMember(
        GymFlowProDbContext ctx, Guid tenantId,
        int referralInviteQuota = 2,
        string membershipStatus = "active",
        DateOnly? start = null,
        DateOnly? end = null)
    {
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
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        var identityUserId = Guid.NewGuid();
        var appUser = new AppUser
        {
            TenantId = tenantId,
            UserId = identityUserId.ToString(),
            FirstName = "Member",
            LastName = "One",
            Email = $"m-{identityUserId:N}@test.local",
            Role = "Member",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(appUser);

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "M-001",
            FullName = "Member One",
            FullNameAr = "عضو",
            PhoneNumber = "+201011111111",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true,
            AppUserId = appUser.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.GymMembers.Add(member);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Growth",
            NameAr = "نمو",
            PlanType = "monthly_unlimited",
            DurationDays = 30,
            Price = 500m,
            ReferralInviteQuota = referralInviteQuota,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.MembershipPlans.Add(plan);

        var today = MembershipOperational.TodayCairo();
        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            StartDate = start ?? today.AddDays(-5),
            EndDate = end ?? today.AddDays(25),
            Status = membershipStatus,
            AmountPaid = 500m,
            PaymentMethod = "cash",
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Memberships.Add(membership);
        ctx.SaveChanges();
        return (identityUserId, member.Id, membership.Id);
    }

    [Fact]
    public async Task Send_Succeeds_ConsumesOneQuota_NationalIdOptional()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, gymMemberId, membershipId) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 15);

        var result = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "Ahmed Mohamed",
            PhoneNumber = "01012345678",
            Notes = "Interested in bodybuilding"
        }, identityUserId, tenantId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Data!.AlreadyExisted);
        Assert.Equal(1, result.Data.QuotaUsed);
        Assert.Equal(14, result.Data.QuotaRemaining);
        Assert.Equal(15, result.Data.QuotaTotal);
        Assert.Equal(InvitationStatuses.New, result.Data.Status);

        var row = await ctx.MemberInvitations.SingleAsync();
        Assert.Equal(gymMemberId, row.InvitingMemberId);
        Assert.Equal(membershipId, row.CoveringMembershipId);
        Assert.Equal(InvitationTypes.Invitation, row.InvitationType);
        Assert.Equal(InvitationStatuses.New, row.Status);
        Assert.Equal("+201012345678", row.GuestPhoneNumber);
        Assert.Null(row.NationalIdEncrypted);
        Assert.Equal("Interested in bodybuilding", row.Notes);
    }

    [Fact]
    public async Task Send_StoresNationalId_WhenProvided()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, _, _) = SeedLinkedMember(ctx, tenantId);

        var result = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "Omar",
            PhoneNumber = "01098765432",
            NationalId = "01010101010101"
        }, identityUserId, tenantId);

        Assert.True(result.IsSuccess, result.Error);
        var row = await ctx.MemberInvitations.SingleAsync();
        Assert.Equal("enc:01010101010101", row.NationalIdEncrypted);
    }

    [Fact]
    public async Task Send_Fails_WhenPassedGymMemberIdInsteadOfIdentity()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (_, gymMemberId, _) = SeedLinkedMember(ctx, tenantId);

        var result = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "Guest",
            PhoneNumber = "01012345678"
        }, gymMemberId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Contains("Member not found", result.Error);
    }

    [Fact]
    public async Task Send_Rejects_ExistingMemberPhone_DoesNotConsumeQuota()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, _, _) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 5);

        ctx.GymMembers.Add(new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "M-002",
            FullName = "Already Member",
            FullNameAr = "عضو",
            PhoneNumber = "+201099999999",
            DateOfBirth = new DateOnly(1992, 2, 2),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var result = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "Already",
            PhoneNumber = "01099999999"
        }, identityUserId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Contains("already a member", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ctx.MemberInvitations);
    }

    [Fact]
    public async Task Send_DuplicatePhone_ReturnsExisting_DoesNotConsumeAgain()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, _, _) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 5);

        var first = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "Ahmed",
            PhoneNumber = "01012345678"
        }, identityUserId, tenantId);
        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(1, first.Data!.QuotaUsed);

        var second = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "Ahmed Again",
            PhoneNumber = "01012345678"
        }, identityUserId, tenantId);
        Assert.True(second.IsSuccess, second.Error);
        Assert.True(second.Data!.AlreadyExisted);
        Assert.Equal(first.Data.InvitationId, second.Data.InvitationId);
        Assert.Equal(1, second.Data.QuotaUsed);
        Assert.Equal(1, await ctx.MemberInvitations.CountAsync());
    }

    [Fact]
    public async Task Send_ZeroQuota_Blocks()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, _, _) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 0);

        var result = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "A",
            PhoneNumber = "01011112222"
        }, identityUserId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Empty(ctx.MemberInvitations);
    }

    [Fact]
    public async Task FrozenMembership_RemainingZero()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, _, _) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 15, membershipStatus: "frozen");

        var meter = await svc.GetMyInvitationSummaryAsync(identityUserId, tenantId);
        Assert.True(meter.IsSuccess);
        Assert.Equal(0, meter.Data!.Remaining);
        Assert.Equal(15, meter.Data.Total);

        var send = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "A",
            PhoneNumber = "01011112222"
        }, identityUserId, tenantId);
        Assert.False(send.IsSuccess);
    }

    [Fact]
    public async Task CancelledMembership_RemainingZero()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, _, _) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 15, membershipStatus: "cancelled");

        var meter = await svc.GetMyInvitationSummaryAsync(identityUserId, tenantId);
        Assert.True(meter.IsSuccess);
        Assert.Equal(0, meter.Data!.Remaining);

        var send = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "A",
            PhoneNumber = "01011112222"
        }, identityUserId, tenantId);
        Assert.False(send.IsSuccess);
    }

    [Fact]
    public async Task ExpiredMembership_RemainingZero()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var today = MembershipOperational.TodayCairo();
        var (identityUserId, _, _) = SeedLinkedMember(
            ctx, tenantId, referralInviteQuota: 15, membershipStatus: "expired",
            start: today.AddDays(-40), end: today.AddDays(-1));

        var meter = await svc.GetMyInvitationSummaryAsync(identityUserId, tenantId);
        Assert.True(meter.IsSuccess);
        Assert.Equal(0, meter.Data!.Remaining);

        var send = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "A",
            PhoneNumber = "01011112222"
        }, identityUserId, tenantId);
        Assert.False(send.IsSuccess);
    }

    [Fact]
    public async Task Renewal_NoCarryOver()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, gymMemberId, oldMembershipId) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 15);

        for (var i = 0; i < 10; i++)
        {
            var send = await svc.SendInvitationAsync(new SendInvitationRequest
            {
                Name = "Friend " + i,
                PhoneNumber = "0101234500" + i
            }, identityUserId, tenantId);
            Assert.True(send.IsSuccess, send.Error);
        }

        var before = await svc.GetMyInvitationSummaryAsync(identityUserId, tenantId);
        Assert.Equal(10, before.Data!.Used);
        Assert.Equal(5, before.Data.Remaining);

        var today = MembershipOperational.TodayCairo();
        var old = await ctx.Memberships.SingleAsync(m => m.Id == oldMembershipId);
        old.Status = "expired";
        old.EndDate = today.AddDays(-1);

        var plan = await ctx.MembershipPlans.SingleAsync();
        var renewal = new Membership
        {
            TenantId = tenantId,
            MemberId = gymMemberId,
            PlanId = plan.Id,
            StartDate = today,
            EndDate = today.AddDays(30),
            Status = "active",
            AmountPaid = 500m,
            PaymentMethod = "cash",
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Memberships.Add(renewal);
        await ctx.SaveChangesAsync();

        var after = await svc.GetMyInvitationSummaryAsync(identityUserId, tenantId);
        Assert.Equal(0, after.Data!.Used);
        Assert.Equal(15, after.Data.Remaining);
        Assert.Equal(renewal.Id, after.Data.MembershipId);
    }

    [Fact]
    public async Task StaffStatusUpdate_AndTenantIsolation()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, _, _) = SeedLinkedMember(ctx, tenantId);

        var send = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "Ahmed Mohamed",
            PhoneNumber = "01012345678"
        }, identityUserId, tenantId);
        Assert.True(send.IsSuccess, send.Error);

        var updated = await svc.UpdateInvitationStatusAsync(
            send.Data!.InvitationId, tenantId, InvitationStatuses.Contacted);
        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal(InvitationStatuses.Contacted, updated.Data!.Status);
        Assert.NotNull(updated.Data.ContactedAtUtc);

        var otherTenant = Guid.NewGuid();
        var isolated = await svc.GetStaffInvitationsAsync(otherTenant, null, null);
        Assert.True(isolated.IsSuccess);
        Assert.Empty(isolated.Data!);
    }

    [Fact]
    public async Task ExpireOverdueGuestPasses_StillMarksHistoricalGuestRows()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var today = MembershipOperational.TodayCairo();
        var (_, memberId, _) = SeedLinkedMember(ctx, tenantId);

        ctx.MemberInvitations.Add(new MemberInvitation
        {
            TenantId = tenantId,
            InvitingMemberId = memberId,
            InvitationType = InvitationTypes.GuestPass,
            GuestName = "Old Guest",
            GuestPhoneNumber = "+201099998888",
            VisitDate = today.AddDays(-2),
            QuotaPeriod = today.AddDays(-2).ToString("yyyy-MM"),
            Status = "pending",
            SentAtUtc = DateTime.UtcNow.AddDays(-3),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3)
        });
        await ctx.SaveChangesAsync();

        var count = await svc.ExpireOverdueGuestPassesAsync();
        Assert.Equal(1, count);
        Assert.Equal("expired", (await ctx.MemberInvitations.SingleAsync()).Status);
    }

    [Fact]
    public async Task StaffSend_UsesGymMemberId_ConsumesQuota()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (_, gymMemberId, membershipId) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 5);

        var result = await svc.SendInvitationForMemberAsync(new SendInvitationRequest
        {
            Name = "Desk Friend",
            PhoneNumber = "01055556666"
        }, gymMemberId, tenantId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Data!.AlreadyExisted);
        Assert.Equal(1, result.Data.QuotaUsed);
        Assert.Equal(4, result.Data.QuotaRemaining);

        var row = await ctx.MemberInvitations.SingleAsync();
        Assert.Equal(gymMemberId, row.InvitingMemberId);
        Assert.Equal(membershipId, row.CoveringMembershipId);
        Assert.Equal(InvitationTypes.Invitation, row.InvitationType);
    }

    [Fact]
    public async Task StaffSend_Fails_UnknownMember()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedLinkedMember(ctx, tenantId);

        var result = await svc.SendInvitationForMemberAsync(new SendInvitationRequest
        {
            Name = "X",
            PhoneNumber = "01012345678"
        }, Guid.NewGuid(), tenantId);

        Assert.False(result.IsSuccess);
        Assert.Contains("Member not found", result.Error);
        Assert.Empty(ctx.MemberInvitations);
    }

    [Fact]
    public async Task StaffSend_Fails_OtherTenantMember()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (_, gymMemberId, _) = SeedLinkedMember(ctx, tenantId);

        var result = await svc.SendInvitationForMemberAsync(new SendInvitationRequest
        {
            Name = "X",
            PhoneNumber = "01012345678"
        }, gymMemberId, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains("Member not found", result.Error);
        Assert.Empty(ctx.MemberInvitations);
    }

    [Fact]
    public async Task StaffSend_FrozenMembership_Blocks()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (_, gymMemberId, _) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 15, membershipStatus: "frozen");

        var result = await svc.SendInvitationForMemberAsync(new SendInvitationRequest
        {
            Name = "A",
            PhoneNumber = "01011112222"
        }, gymMemberId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Empty(ctx.MemberInvitations);
    }

    [Fact]
    public async Task RefundThenSamePlanRenewal_InvitationUsesNewCoveringMembership()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, gymMemberId, oldMembershipId) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 2);

        var before = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "Before Refund",
            PhoneNumber = "01011110001"
        }, identityUserId, tenantId);
        Assert.True(before.IsSuccess, before.Error);

        var originalInvite = await ctx.MemberInvitations.SingleAsync();
        Assert.Equal(oldMembershipId, originalInvite.CoveringMembershipId);

        var old = await ctx.Memberships.SingleAsync(m => m.Id == oldMembershipId);
        old.Status = "cancelled";
        await ctx.SaveChangesAsync();

        var afterRefund = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "After Refund",
            PhoneNumber = "01011110002"
        }, identityUserId, tenantId);
        Assert.False(afterRefund.IsSuccess);
        Assert.Contains("No active membership", afterRefund.Error);

        var afterRefundMeter = await svc.GetMyInvitationSummaryAsync(identityUserId, tenantId);
        Assert.True(afterRefundMeter.IsSuccess);
        Assert.Equal(0, afterRefundMeter.Data!.Remaining);
        Assert.Null(afterRefundMeter.Data.MembershipId);

        var today = MembershipOperational.TodayCairo();
        var plan = await ctx.MembershipPlans.SingleAsync();
        var renewal = new Membership
        {
            TenantId = tenantId,
            MemberId = gymMemberId,
            PlanId = plan.Id,
            StartDate = today,
            EndDate = today.AddDays(plan.DurationDays),
            Status = "active",
            AmountPaid = 500m,
            PaymentMethod = "cash",
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Memberships.Add(renewal);
        await ctx.SaveChangesAsync();

        var afterRenew = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "After Renew",
            PhoneNumber = "01011110003"
        }, identityUserId, tenantId);
        Assert.True(afterRenew.IsSuccess, afterRenew.Error);
        Assert.Equal(1, afterRenew.Data!.QuotaUsed);
        Assert.Equal(1, afterRenew.Data.QuotaRemaining);

        var newInvite = await ctx.MemberInvitations.SingleAsync(i => i.GuestName == "After Renew");
        Assert.Equal(renewal.Id, newInvite.CoveringMembershipId);

        var historical = await ctx.MemberInvitations.SingleAsync(i => i.Id == originalInvite.Id);
        Assert.Equal(oldMembershipId, historical.CoveringMembershipId);
        Assert.Equal(InvitationStatuses.New, historical.Status);

        Assert.Equal("cancelled", (await ctx.Memberships.SingleAsync(m => m.Id == oldMembershipId)).Status);
        Assert.Equal("active", (await ctx.Memberships.SingleAsync(m => m.Id == renewal.Id)).Status);

        var meter = await svc.GetMyInvitationSummaryAsync(identityUserId, tenantId);
        Assert.Equal(renewal.Id, meter.Data!.MembershipId);
        Assert.Equal(1, meter.Data.Used);
        Assert.Equal(1, meter.Data.Remaining);
    }

    [Fact]
    public async Task ExpiredThenSamePlanRenewal_InvitationWorks()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var today = MembershipOperational.TodayCairo();
        var (identityUserId, gymMemberId, oldId) = SeedLinkedMember(
            ctx, tenantId, referralInviteQuota: 3, membershipStatus: "expired",
            start: today.AddDays(-40), end: today.AddDays(-1));

        var blocked = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "Expired",
            PhoneNumber = "01011110010"
        }, identityUserId, tenantId);
        Assert.False(blocked.IsSuccess);

        var plan = await ctx.MembershipPlans.SingleAsync();
        ctx.Memberships.Add(new Membership
        {
            TenantId = tenantId,
            MemberId = gymMemberId,
            PlanId = plan.Id,
            StartDate = today,
            EndDate = today.AddDays(30),
            Status = "active",
            AmountPaid = 500m,
            PaymentMethod = "cash",
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var send = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "After Expired Renew",
            PhoneNumber = "01011110011"
        }, identityUserId, tenantId);
        Assert.True(send.IsSuccess, send.Error);
        Assert.NotEqual(oldId, (await ctx.MemberInvitations.SingleAsync()).CoveringMembershipId);
    }

    [Fact]
    public async Task RenewDifferentPlan_InvitationUsesNewPlanQuota()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, gymMemberId, oldId) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 1);

        var first = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "Used Old Quota",
            PhoneNumber = "01011110020"
        }, identityUserId, tenantId);
        Assert.True(first.IsSuccess, first.Error);

        var exhausted = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "Blocked",
            PhoneNumber = "01011110021"
        }, identityUserId, tenantId);
        Assert.False(exhausted.IsSuccess);

        var today = MembershipOperational.TodayCairo();
        var old = await ctx.Memberships.SingleAsync(m => m.Id == oldId);
        old.Status = "expired";
        old.EndDate = today.AddDays(-1);

        var otherPlan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Premium",
            NameAr = "مميز",
            PlanType = "monthly_unlimited",
            DurationDays = 30,
            Price = 800m,
            ReferralInviteQuota = 4,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.MembershipPlans.Add(otherPlan);
        ctx.Memberships.Add(new Membership
        {
            TenantId = tenantId,
            MemberId = gymMemberId,
            PlanId = otherPlan.Id,
            StartDate = today,
            EndDate = today.AddDays(30),
            Status = "active",
            AmountPaid = 800m,
            PaymentMethod = "cash",
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var send = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "New Plan Guest",
            PhoneNumber = "01011110022"
        }, identityUserId, tenantId);
        Assert.True(send.IsSuccess, send.Error);
        Assert.Equal(3, send.Data!.QuotaRemaining);
        Assert.Equal(4, send.Data.QuotaTotal);
    }

    [Fact]
    public async Task MultipleRenewals_InvitationAlwaysUsesLatestCovering()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var (identityUserId, gymMemberId, firstId) = SeedLinkedMember(ctx, tenantId, referralInviteQuota: 2);
        var today = MembershipOperational.TodayCairo();
        var plan = await ctx.MembershipPlans.SingleAsync();

        Guid coveringId = firstId;
        for (var n = 0; n < 3; n++)
        {
            var prior = await ctx.Memberships.SingleAsync(m => m.Id == coveringId);
            prior.Status = "expired";
            prior.EndDate = today.AddDays(-1);
            var next = new Membership
            {
                TenantId = tenantId,
                MemberId = gymMemberId,
                PlanId = plan.Id,
                StartDate = today,
                EndDate = today.AddDays(30),
                Status = "active",
                AmountPaid = 500m,
                PaymentMethod = "cash",
                CreatedAtUtc = DateTime.UtcNow
            };
            ctx.Memberships.Add(next);
            await ctx.SaveChangesAsync();
            coveringId = next.Id;
        }

        var send = await svc.SendInvitationAsync(new SendInvitationRequest
        {
            Name = "After Three Renewals",
            PhoneNumber = "01011110030"
        }, identityUserId, tenantId);
        Assert.True(send.IsSuccess, send.Error);
        Assert.Equal(coveringId, (await ctx.MemberInvitations.SingleAsync()).CoveringMembershipId);
        Assert.Equal(1, send.Data!.QuotaUsed);
        Assert.Equal(1, send.Data.QuotaRemaining);
    }
}
