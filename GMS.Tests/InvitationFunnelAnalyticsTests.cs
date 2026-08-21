namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class InvitationFunnelAnalyticsTests
{
    [Fact]
    public async Task Funnel_CountsInvitationProduct_AndKeepsHistoricalSlices()
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
            Id = tenantId, Name = "Gym", NameAr = "ص", GymCode = "GYMFUN01",
            City = "Cairo", Address = "x", PhoneNumber = "01000000000",
            Email = "f@t.local", SubscriptionStartDate = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
        });

        var today = MembershipOperational.TodayCairo();
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        var monthStartLocal = new DateOnly(today.Year, today.Month, 1).ToDateTime(TimeOnly.MinValue);
        var midMonthUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(monthStartLocal.AddDays(5), DateTimeKind.Unspecified), cairoTz);

        ctx.MemberInvitations.Add(new MemberInvitation
        {
            TenantId = tenantId, InvitingMemberId = Guid.NewGuid(),
            InvitationType = InvitationTypes.Invitation, GuestName = "A1", GuestPhoneNumber = "+201011111111",
            Status = InvitationStatuses.New,
            SentAtUtc = midMonthUtc, CreatedAtUtc = midMonthUtc, QuotaPeriod = string.Empty
        });
        var convertedMemberId = Guid.NewGuid();
        ctx.MemberInvitations.Add(new MemberInvitation
        {
            TenantId = tenantId, InvitingMemberId = Guid.NewGuid(),
            InvitationType = InvitationTypes.Invitation, GuestName = "A2", GuestPhoneNumber = "+201022222222",
            Status = InvitationStatuses.Converted, ConvertedMemberId = convertedMemberId, ConvertedAtUtc = midMonthUtc,
            SentAtUtc = midMonthUtc, CreatedAtUtc = midMonthUtc, QuotaPeriod = string.Empty
        });

        ctx.MemberInvitations.Add(new MemberInvitation
        {
            TenantId = tenantId, InvitingMemberId = Guid.NewGuid(),
            InvitationType = InvitationTypes.GuestPass, GuestName = "G1", GuestPhoneNumber = "+201033333333",
            Status = "visited", VisitDate = today, VisitedAtUtc = midMonthUtc,
            SentAtUtc = midMonthUtc, CreatedAtUtc = midMonthUtc, QuotaPeriod = today.ToString("yyyy-MM")
        });
        ctx.MemberInvitations.Add(new MemberInvitation
        {
            TenantId = tenantId, InvitingMemberId = Guid.NewGuid(),
            InvitationType = InvitationTypes.Referral, GuestName = "R1", GuestPhoneNumber = "+201044444444",
            Status = "pending", SentAtUtc = midMonthUtc, CreatedAtUtc = midMonthUtc, QuotaPeriod = string.Empty
        });

        ctx.GymMembers.Add(new GymMember
        {
            Id = convertedMemberId, TenantId = tenantId, MemberNumber = "M1",
            FullName = "New1", FullNameAr = "ن", PhoneNumber = "+201022222222",
            DateOfBirth = new DateOnly(1990, 1, 1), IsActive = true, CreatedAtUtc = midMonthUtc
        });
        ctx.GymMembers.Add(new GymMember
        {
            TenantId = tenantId, MemberNumber = "M2",
            FullName = "New2", FullNameAr = "ن٢", PhoneNumber = "+201055555555",
            DateOfBirth = new DateOnly(1991, 1, 1), IsActive = true, CreatedAtUtc = midMonthUtc
        });

        await ctx.SaveChangesAsync();

        var svc = new AnalyticsService(ctx, NullLogger<AnalyticsService>.Instance);
        var result = await svc.GetInvitationFunnelAsync(tenantId);
        Assert.True(result.IsSuccess, result.Error);

        var dto = result.Data!;
        Assert.Equal(2, dto.Sent);
        Assert.Equal(1, dto.New);
        Assert.Equal(1, dto.Converted);
        Assert.Equal(1, dto.GuestPass.Sent);
        Assert.Equal(1, dto.Referral.Sent);
        Assert.Equal(2, dto.NewMembersThisMonth);
        Assert.Equal(1, dto.ReferralConvertedMembersThisMonth);
        Assert.Equal(50m, dto.PercentNewMembersFromReferrals);
    }
}
