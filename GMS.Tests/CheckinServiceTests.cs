namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Attendance;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Enums;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;

public class CheckinServiceTests
{
    private class NoOpCheckinNotifier : ICheckinNotifier
    {
        public Task NotifyCheckinAsync(Guid tenantId, Guid memberId, string memberName,
            string memberNumber, DateTime checkInTime, string entryMethod) => Task.CompletedTask;
    }

    private static (GymFlowProDbContext ctx, CheckinService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var auditService = new AuditService(ctx, new Microsoft.AspNetCore.Http.HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);

        var svc = new CheckinService(
            ctx, new MemberRepository(ctx), new AttendanceRepository(ctx),
            new MemoryCache(new MemoryCacheOptions()), new NoOpCheckinNotifier(),
            auditService, NullLogger<CheckinService>.Instance);

        return (ctx, svc, tenantId);
    }

    private static void SeedTenant(GymFlowProDbContext ctx, Guid tenantId)
    {
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
            SubscriptionStartDate = DateTime.UtcNow
        });
    }

    /// <summary>Returns the Identity id (JWT "sub") — NOT AppUser.Id — since that's what
    /// CheckinService's staffUserId parameter is compared against (via AppUser.UserId).</summary>
    private static Guid SeedStaff(GymFlowProDbContext ctx, Guid tenantId)
    {
        var identityUserId = Guid.NewGuid();
        var staff = new AppUser
        {
            TenantId = tenantId,
            UserId = identityUserId.ToString(),
            FirstName = "Front",
            LastName = "Desk",
            Email = $"staff-{Guid.NewGuid()}@test.local",
            Role = "Receptionist"
        };
        ctx.AppUsers.Add(staff);
        return identityUserId;
    }

    [Fact]
    public async Task ProcessManualCheckinAsync_TrialVisitLimitReached_BlocksFourthCheckin()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffIdentityId = SeedStaff(ctx, tenantId);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Free Trial",
            NameAr = "تجربة مجانية",
            PlanType = "trial",
            DurationDays = 14,
            Price = 0m,
            TrialVisitLimit = 3
        };
        ctx.MembershipPlans.Add(plan);

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-001",
            FullName = "Trial Member",
            FullNameAr = "عضو تجريبي",
            PhoneNumber = "+201001234567",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25)),
            IsActive = true,
            IsTrial = true,
            TrialOutcome = "active_trial"
        };
        ctx.GymMembers.Add(member);

        // Trial started 10 days ago so the 3 prior visits (seeded on past days below) can be
        // backdated without landing on "today" and tripping the unrelated duplicate-checkin guard.
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            StartDate = startDate,
            EndDate = startDate.AddDays(plan.DurationDays),
            Status = "active"
        };
        ctx.Memberships.Add(membership);

        await ctx.SaveChangesAsync();

        // 3 prior visits (on past days, not today) already consumed the trial's visit cap.
        for (var day = 3; day >= 1; day--)
        {
            ctx.GymAttendances.Add(new GymAttendance
            {
                TenantId = tenantId,
                MemberId = member.Id,
                MembershipId = membership.Id,
                CheckInAtUtc = DateTime.UtcNow.Date.AddDays(-day).AddHours(10),
                EntryMethod = "manual"
            });
        }
        await ctx.SaveChangesAsync();

        var result = await svc.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            staffIdentityId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Contains("TRIAL_VISITS_EXHAUSTED", result.Error);
    }

    [Fact]
    public async Task ProcessManualCheckinAsync_TrialUnderVisitLimit_Succeeds()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffIdentityId = SeedStaff(ctx, tenantId);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Free Trial",
            NameAr = "تجربة مجانية",
            PlanType = "trial",
            DurationDays = 14,
            Price = 0m,
            TrialVisitLimit = 3
        };
        ctx.MembershipPlans.Add(plan);

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-002",
            FullName = "Trial Member Two",
            FullNameAr = "عضو تجريبي اثنان",
            PhoneNumber = "+201009876543",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25)),
            IsActive = true,
            IsTrial = true,
            TrialOutcome = "active_trial"
        };
        ctx.GymMembers.Add(member);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            StartDate = startDate,
            EndDate = startDate.AddDays(plan.DurationDays),
            Status = "active"
        };
        ctx.Memberships.Add(membership);

        await ctx.SaveChangesAsync();

        // Only 2 prior visits (on past days, not today) — under the cap of 3, so today's
        // check-in (the 3rd) should succeed.
        for (var day = 2; day >= 1; day--)
        {
            ctx.GymAttendances.Add(new GymAttendance
            {
                TenantId = tenantId,
                MemberId = member.Id,
                MembershipId = membership.Id,
                CheckInAtUtc = DateTime.UtcNow.Date.AddDays(-day).AddHours(10),
                EntryMethod = "manual"
            });
        }
        await ctx.SaveChangesAsync();

        var result = await svc.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            staffIdentityId, tenantId);

        Assert.True(result.IsSuccess, result.Error);
    }
}
