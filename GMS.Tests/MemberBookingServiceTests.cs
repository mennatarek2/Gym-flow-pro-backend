namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// End-to-end coverage for the Member App booking surface (MemberBookingService), exercising
/// the real identity chain: Identity user id (JWT sub) -> AppUser.UserId (string) -> AppUser.Id
/// -> GymMember.AppUserId. Regression guard for a bug where ResolveMemberIdAsync compared the
/// Identity id directly against GymMember.AppUserId (a different id space), which made every
/// Member App booking call fail with "Member profile not found" for real members.
/// </summary>
public class MemberBookingServiceTests
{
    private readonly Guid _tenantA = Guid.NewGuid();

    private (GymFlowProDbContext Db, ITenantContext Tenant) NewContext(Guid tenantId)
    {
        var tenant = new Infrastructure.Services.TenantContext();
        tenant.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new GymFlowProDbContext(options, tenant);
        return (db, tenant);
    }

    private static ActivityEntitlementService Entitlements(GymFlowProDbContext db) => new(db);

    private static SessionBookingService BookingService(GymFlowProDbContext db) =>
        new(db, Entitlements(db), new SessionGenerationService(db, NullLogger<SessionGenerationService>.Instance), NullLogger<SessionBookingService>.Instance);

    private static MemberBookingService MemberService(GymFlowProDbContext db) =>
        new(db, Entitlements(db), BookingService(db));

    private static SessionGenerationService Generator(GymFlowProDbContext db) =>
        new(db, NullLogger<SessionGenerationService>.Instance);

    /// <summary>
    /// Seeds a member with the *real* identity chain a Member App login actually produces
    /// (see AuthService.FindOrCreateMemberAppUserAsync / LinkGymMemberToAppUserAsync):
    /// GymMember.AppUserId -> AppUser.Id, AppUser.UserId -> identityUserId.ToString().
    /// Returns the identityUserId a controller would read from the JWT sub claim.
    /// </summary>
    private async Task<(Guid IdentityUserId, GymMember Member)> SeedMemberWithIdentityAsync(
        GymFlowProDbContext db, Guid tenantId, Guid planId)
    {
        var identityUserId = Guid.NewGuid();
        var appUser = new AppUser
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = identityUserId.ToString(),
            FirstName = "Test", LastName = "Member", Email = $"member-{Guid.NewGuid()}@test.local",
            PhoneNumber = "+201000000000", Role = "Member"
        };
        var member = new GymMember
        {
            Id = Guid.NewGuid(), TenantId = tenantId, FullName = "Test Member",
            PhoneNumber = "+201000000000", AppUserId = appUser.Id
        };
        var membership = new Membership
        {
            Id = Guid.NewGuid(), TenantId = tenantId, MemberId = member.Id, PlanId = planId,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            Status = "active"
        };
        db.AppUsers.Add(appUser);
        db.GymMembers.Add(member);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();
        return (identityUserId, member);
    }

    private async Task<(MembershipPlan Plan, Activity Activity)> SeedPlanAndActivityAsync(GymFlowProDbContext db, Guid tenantId)
    {
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Gold", Price = 500, DurationDays = 30, IsActive = true
        };
        var activity = new Activity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "CrossFit", Kind = Core.Constants.ActivityKinds.Class,
            BookingRequired = true, VisibleToMembers = true, IsActive = true
        };
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "T", IsActive = true });
        db.MembershipPlans.Add(plan);
        db.Activities.Add(activity);
        db.PlanEntitlements.Add(new PlanEntitlement
        {
            TenantId = tenantId, PlanId = plan.Id, ActivityId = activity.Id, AccessMode = "included"
        });
        await db.SaveChangesAsync();
        return (plan, activity);
    }

    private async Task<ActivitySession> SeedSessionAsync(GymFlowProDbContext db, Guid tenantId, Guid activityId, DateTime startsUtc)
    {
        var cairo = TimeZoneInfo.ConvertTimeFromUtc(startsUtc, TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"));
        var schedule = new ActivitySchedule
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActivityId = activityId,
            DaysOfWeek = $"[{(int)cairo.DayOfWeek}]",
            StartTime = TimeOnly.FromDateTime(cairo), EndTime = TimeOnly.FromDateTime(cairo).AddHours(1),
            Capacity = 15,
            EffectiveFrom = DateOnly.FromDateTime(cairo.Date.AddDays(-1)),
            EffectiveUntil = DateOnly.FromDateTime(cairo.Date.AddDays(1))
        };
        db.ActivitySchedules.Add(schedule);
        await db.SaveChangesAsync();
        await Generator(db).GenerateUpcomingSessionsAsync(tenantId);
        return await db.ActivitySessions.SingleAsync(s => s.ScheduleId == schedule.Id && s.StartsAtUtc == startsUtc);
    }

    [Fact]
    public async Task Member_Can_Discover_Book_And_See_Own_Booking_Via_Real_Identity_Chain()
    {
        var (db, _) = NewContext(_tenantA);
        var (plan, activity) = await SeedPlanAndActivityAsync(db, _tenantA);
        var (identityUserId, _) = await SeedMemberWithIdentityAsync(db, _tenantA, plan.Id);
        var session = await SeedSessionAsync(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(3));

        var svc = MemberService(db);

        var activities = await svc.ListActivitiesAsync(_tenantA, identityUserId);
        Assert.True(activities.IsSuccess, activities.Error);
        Assert.Contains(activities.Data!, a => a.Id == activity.Id && a.Eligibility == "included");

        var sessions = await svc.ListUpcomingSessionsAsync(_tenantA, identityUserId, null, null);
        Assert.True(sessions.IsSuccess, sessions.Error);
        Assert.Contains(sessions.Data!, s => s.Id == session.Id && s.CanBook);

        var book = await svc.BookAsync(_tenantA, identityUserId, session.Id);
        Assert.True(book.IsSuccess, book.Error);

        var mine = await svc.MyBookingsAsync(_tenantA, identityUserId);
        Assert.True(mine.IsSuccess, mine.Error);
        Assert.Single(mine.Data!);
        Assert.Equal(session.Id, mine.Data![0].SessionId);

        var cancel = await svc.CancelOwnAsync(_tenantA, identityUserId, book.Data!.Id);
        Assert.True(cancel.IsSuccess, cancel.Error);
    }

    [Fact]
    public async Task Unknown_Identity_Cannot_Resolve_To_Any_Member()
    {
        var (db, _) = NewContext(_tenantA);
        await SeedPlanAndActivityAsync(db, _tenantA);

        var svc = MemberService(db);
        var result = await svc.ListActivitiesAsync(_tenantA, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Member_Cannot_See_Or_Cancel_Another_Members_Booking_Through_Member_Api()
    {
        var (db, _) = NewContext(_tenantA);
        var (plan, activity) = await SeedPlanAndActivityAsync(db, _tenantA);
        var (identityA, _) = await SeedMemberWithIdentityAsync(db, _tenantA, plan.Id);
        var (identityB, _) = await SeedMemberWithIdentityAsync(db, _tenantA, plan.Id);
        var session = await SeedSessionAsync(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(3));

        var svc = MemberService(db);
        var book = await svc.BookAsync(_tenantA, identityA, session.Id);
        Assert.True(book.IsSuccess, book.Error);

        var viewAsB = await svc.MyBookingAsync(_tenantA, identityB, book.Data!.Id);
        Assert.False(viewAsB.IsSuccess);

        var cancelAsB = await svc.CancelOwnAsync(_tenantA, identityB, book.Data.Id);
        Assert.False(cancelAsB.IsSuccess);

        var stillBooked = await svc.MyBookingAsync(_tenantA, identityA, book.Data.Id);
        Assert.True(stillBooked.IsSuccess);
        Assert.Equal("booked", stillBooked.Data!.Status);
    }
}
