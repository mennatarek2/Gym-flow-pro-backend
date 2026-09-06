namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Attendance;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Enums;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
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

    private static (GymFlowProDbContext ctx, CheckinService svc, Guid tenantId, IMemoryCache cache) CreateSut()
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var auditService = new AuditService(ctx, new Microsoft.AspNetCore.Http.HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var cache = new MemoryCache(new MemoryCacheOptions());

        var svc = new CheckinService(
            ctx, new MemberRepository(ctx), new AttendanceRepository(ctx),
            cache, new NoOpCheckinNotifier(),
            auditService, NullLogger<CheckinService>.Instance);

        return (ctx, svc, tenantId, cache);
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
        var (ctx, svc, tenantId, _) = CreateSut();
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
        var (ctx, svc, tenantId, _) = CreateSut();
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

    [Fact]
    public async Task ProcessManualCheckinAsync_SessionPack_DecrementsSessions()
    {
        var (ctx, svc, tenantId, _) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffIdentityId = SeedStaff(ctx, tenantId);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "10 Sessions",
            NameAr = "10 جلسات",
            PlanType = "session_pack",
            DurationDays = 90,
            Price = 800m,
            SessionCount = 10
        };
        ctx.MembershipPlans.Add(plan);

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-SP-01",
            FullName = "Session Pack Member",
            FullNameAr = "عضو باقة جلسات",
            PhoneNumber = "+201001112233",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-28)),
            IsActive = true
        };
        ctx.GymMembers.Add(member);

        var today = MembershipOperational.TodayCairo();
        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            Plan = plan,
            StartDate = today.AddDays(-5),
            EndDate = today.AddDays(85),
            Status = "active",
            SessionsRemaining = 3
        };
        ctx.Memberships.Add(membership);
        await ctx.SaveChangesAsync();

        var result = await svc.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            staffIdentityId, tenantId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Data!.SessionsRemaining);
        Assert.Equal(2, (await ctx.Memberships.SingleAsync()).SessionsRemaining);
        Assert.Equal(1, await ctx.GymAttendances.CountAsync());
    }

    [Fact]
    public async Task ProcessManualCheckinAsync_ZeroSessions_Rejected_DoesNotWriteAttendance()
    {
        var (ctx, svc, tenantId, _) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffIdentityId = SeedStaff(ctx, tenantId);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "10 Sessions",
            NameAr = "10 جلسات",
            PlanType = "session_pack",
            DurationDays = 90,
            Price = 800m,
            SessionCount = 10
        };
        ctx.MembershipPlans.Add(plan);

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-SP-00",
            FullName = "Empty Pack",
            FullNameAr = "باقة فارغة",
            PhoneNumber = "+201004445566",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-30)),
            IsActive = true
        };
        ctx.GymMembers.Add(member);

        var today = MembershipOperational.TodayCairo();
        ctx.Memberships.Add(new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            Plan = plan,
            StartDate = today.AddDays(-5),
            EndDate = today.AddDays(85),
            Status = "active",
            SessionsRemaining = 0
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            staffIdentityId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Contains("sessions", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await ctx.GymAttendances.CountAsync());
        Assert.Equal(0, (await ctx.Memberships.SingleAsync()).SessionsRemaining);
    }

    [Fact]
    public async Task ProcessManualCheckinAsync_BeforeStartDate_Rejected()
    {
        var (ctx, svc, tenantId, _) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffIdentityId = SeedStaff(ctx, tenantId);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Monthly",
            NameAr = "شهرية",
            PlanType = "duration",
            DurationDays = 30,
            Price = 500m
        };
        ctx.MembershipPlans.Add(plan);

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-FUTURE",
            FullName = "Future Start",
            FullNameAr = "بداية مستقبلية",
            PhoneNumber = "+201007778899",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-22)),
            IsActive = true
        };
        ctx.GymMembers.Add(member);

        var today = MembershipOperational.TodayCairo();
        // Stored active but starts tomorrow — GetActiveMembershipCached filters StartDate <= today,
        // so check-in should fail with no covering membership.
        ctx.Memberships.Add(new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            Plan = plan,
            StartDate = today.AddDays(1),
            EndDate = today.AddDays(31),
            Status = "active"
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            staffIdentityId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, await ctx.GymAttendances.CountAsync());
    }

    [Fact]
    public async Task ProcessManualCheckinAsync_InactiveMember_Rejected()
    {
        var (ctx, svc, tenantId, _) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffIdentityId = SeedStaff(ctx, tenantId);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Monthly",
            NameAr = "شهرية",
            PlanType = "duration",
            DurationDays = 30,
            Price = 500m
        };
        ctx.MembershipPlans.Add(plan);

        var today = MembershipOperational.TodayCairo();
        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-INACTIVE",
            FullName = "Inactive Member",
            FullNameAr = "عضو غير نشط",
            PhoneNumber = "+201001112200",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25)),
            IsActive = false
        };
        ctx.GymMembers.Add(member);
        ctx.Memberships.Add(new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            Plan = plan,
            StartDate = today.AddDays(-5),
            EndDate = today.AddDays(25),
            Status = "active"
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            staffIdentityId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Contains("inactive", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await ctx.GymAttendances.CountAsync());
    }

    [Fact]
    public async Task ProcessManualCheckinAsync_FrozenMembership_Rejected()
    {
        var (ctx, svc, tenantId, _) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffIdentityId = SeedStaff(ctx, tenantId);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Monthly",
            NameAr = "شهرية",
            PlanType = "duration",
            DurationDays = 30,
            Price = 500m
        };
        ctx.MembershipPlans.Add(plan);

        var today = MembershipOperational.TodayCairo();
        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-FROZEN",
            FullName = "Frozen Member",
            FullNameAr = "عضو مجمد",
            PhoneNumber = "+201001112201",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25)),
            IsActive = true
        };
        ctx.GymMembers.Add(member);
        ctx.Memberships.Add(new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            Plan = plan,
            StartDate = today.AddDays(-5),
            EndDate = today.AddDays(25),
            Status = "frozen"
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            staffIdentityId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Contains("frozen", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await ctx.GymAttendances.CountAsync());
    }

    [Fact]
    public async Task ProcessManualCheckinAsync_AfterEndDate_Rejected()
    {
        var (ctx, svc, tenantId, _) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffIdentityId = SeedStaff(ctx, tenantId);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Monthly",
            NameAr = "شهرية",
            PlanType = "duration",
            DurationDays = 30,
            Price = 500m
        };
        ctx.MembershipPlans.Add(plan);

        var today = MembershipOperational.TodayCairo();
        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-EXPIRED",
            FullName = "Expired Member",
            FullNameAr = "منتهي",
            PhoneNumber = "+201001112202",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25)),
            IsActive = true
        };
        ctx.GymMembers.Add(member);
        // Status still "active" but EndDate in the past — cache filter excludes it.
        ctx.Memberships.Add(new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            Plan = plan,
            StartDate = today.AddDays(-40),
            EndDate = today.AddDays(-1),
            Status = "active"
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            staffIdentityId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, await ctx.GymAttendances.CountAsync());
    }

    [Fact]
    public async Task ProcessManualCheckinAsync_DuplicateSameDay_Rejected()
    {
        var (ctx, svc, tenantId, _) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffIdentityId = SeedStaff(ctx, tenantId);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Monthly",
            NameAr = "شهرية",
            PlanType = "duration",
            DurationDays = 30,
            Price = 500m
        };
        ctx.MembershipPlans.Add(plan);

        var today = MembershipOperational.TodayCairo();
        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-DUP",
            FullName = "Dup Member",
            FullNameAr = "مكرر",
            PhoneNumber = "+201001112203",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25)),
            IsActive = true
        };
        ctx.GymMembers.Add(member);
        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            Plan = plan,
            StartDate = today.AddDays(-5),
            EndDate = today.AddDays(25),
            Status = "active"
        };
        ctx.Memberships.Add(membership);
        await ctx.SaveChangesAsync();

        var first = await svc.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            staffIdentityId, tenantId);
        Assert.True(first.IsSuccess, first.Error);

        var second = await svc.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            staffIdentityId, tenantId);
        Assert.False(second.IsSuccess);
        Assert.Contains("already", second.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await ctx.GymAttendances.CountAsync());
    }

    [Fact]
    public async Task ProcessManualCheckinAsync_FailedSessionDecrement_CompensatesAttendance()
    {
        var (ctx, svc, tenantId, cache) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffIdentityId = SeedStaff(ctx, tenantId);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "10 Sessions",
            NameAr = "10 جلسات",
            PlanType = "session_pack",
            DurationDays = 90,
            Price = 800m,
            SessionCount = 10
        };
        ctx.MembershipPlans.Add(plan);

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-SP-COMP",
            FullName = "Compensate Member",
            FullNameAr = "تعويض",
            PhoneNumber = "+201009990011",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-26)),
            IsActive = true
        };
        ctx.GymMembers.Add(member);

        var today = MembershipOperational.TodayCairo();
        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            Plan = plan,
            StartDate = today.AddDays(-1),
            EndDate = today.AddDays(89),
            Status = "active",
            SessionsRemaining = 0
        };
        ctx.Memberships.Add(membership);
        await ctx.SaveChangesAsync();

        // Stale cache: validation sees SessionsRemaining > 0, DB truth is 0 → decrement fails → compensate.
        var staleCached = new Membership
        {
            Id = membership.Id,
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            Plan = plan,
            StartDate = membership.StartDate,
            EndDate = membership.EndDate,
            Status = "active",
            SessionsRemaining = 1
        };
        cache.Set(
            $"membership:{tenantId}:{member.Id}",
            staleCached,
            new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(5)));

        var result = await svc.ProcessManualCheckinAsync(
            new ManualCheckinRequest { MemberId = member.Id, Reason = ManualCheckinReason.NoAppYet },
            staffIdentityId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.Contains("sessions", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await ctx.GymAttendances.IgnoreQueryFilters()
            .CountAsync(a => a.TenantId == tenantId && !a.IsDeleted));
        Assert.Equal(0, (await ctx.Memberships.SingleAsync()).SessionsRemaining);
        // Compensated soft-delete left an IsDeleted row (proves write-then-compensate, not pre-reject).
        Assert.Equal(1, await ctx.GymAttendances.IgnoreQueryFilters()
            .CountAsync(a => a.TenantId == tenantId && a.IsDeleted));
    }
}
