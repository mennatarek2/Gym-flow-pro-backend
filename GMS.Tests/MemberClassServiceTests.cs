namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Read-only Member App classes (MemberClassService). Ensures browsing never creates bookings or payments.
/// </summary>
public class MemberClassServiceTests
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

    private static MemberClassService ClassService(GymFlowProDbContext db) =>
        new(db, new SessionGenerationService(db, NullLogger<SessionGenerationService>.Instance),
            NullLogger<MemberClassService>.Instance);

    private async Task<(Guid IdentityUserId, GymMember Member)> SeedMemberWithIdentityAsync(
        GymFlowProDbContext db, Guid tenantId)
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
        db.AppUsers.Add(appUser);
        db.GymMembers.Add(member);
        await db.SaveChangesAsync();
        return (identityUserId, member);
    }

    private async Task<(Activity ClassActivity, AppUser Coach)> SeedClassActivityAsync(
        GymFlowProDbContext db, Guid tenantId, decimal? dropInPrice = 150m)
    {
        var coach = new AppUser
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid().ToString(),
            FirstName = "Coach", LastName = "Sam", Email = "coach@test.local",
            PhoneNumber = "+201000000001", Role = "trainer"
        };
        var activity = new Activity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Yoga Flow", NameAr = "يوغا",
            Description = "Morning yoga", DescriptionAr = "يوغا صباحية",
            Kind = ActivityKinds.Class, BookingRequired = true, VisibleToMembers = true,
            IsActive = true, DropInPrice = dropInPrice, DefaultCapacity = 20
        };
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "T", IsActive = true });
        db.AppUsers.Add(coach);
        db.Activities.Add(activity);
        await db.SaveChangesAsync();
        return (activity, coach);
    }

    private async Task<ActivitySession> SeedUpcomingSessionAsync(
        GymFlowProDbContext db, Guid tenantId, Guid activityId, Guid? coachUserId,
        DateTime startsUtc, int capacity = 20, string status = ActivitySessionStatuses.Upcoming)
    {
        var session = new ActivitySession
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActivityId = activityId,
            StartsAtUtc = startsUtc, EndsAtUtc = startsUtc.AddHours(1),
            Capacity = capacity, CoachUserId = coachUserId, Status = status
        };
        db.ActivitySessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    [Fact]
    public async Task Member_Can_List_Upcoming_Classes_With_Real_Data()
    {
        var (db, _) = NewContext(_tenantA);
        var (activity, coach) = await SeedClassActivityAsync(db, _tenantA, 200m);
        var (identityUserId, _) = await SeedMemberWithIdentityAsync(db, _tenantA);
        var starts = DateTime.UtcNow.AddHours(4);
        var session = await SeedUpcomingSessionAsync(db, _tenantA, activity.Id, coach.Id, starts, capacity: 15);

        var svc = ClassService(db);
        var result = await svc.ListUpcomingAsync(_tenantA, identityUserId);

        Assert.True(result.IsSuccess, result.Error);
        var row = Assert.Single(result.Data!, r => r.Id == session.Id);
        Assert.Equal(session.Id, row.Id);
        Assert.Equal("Yoga Flow", row.Name);
        Assert.Equal("Morning yoga", row.Description);
        Assert.Equal(coach.Id, row.TrainerId);
        Assert.Equal("Coach Sam", row.TrainerName);
        Assert.Equal(200m, row.Price);
        Assert.Equal(15, row.Capacity);
        Assert.Equal(15, row.AvailableSeats);
        Assert.Equal(ActivitySessionStatuses.Upcoming, row.Status);
        Assert.True(row.DurationMinutes > 0);
    }

    [Fact]
    public async Task Member_Can_Get_Class_Details()
    {
        var (db, _) = NewContext(_tenantA);
        var (activity, coach) = await SeedClassActivityAsync(db, _tenantA);
        var (identityUserId, _) = await SeedMemberWithIdentityAsync(db, _tenantA);
        var session = await SeedUpcomingSessionAsync(db, _tenantA, activity.Id, coach.Id, DateTime.UtcNow.AddHours(5), capacity: 10);

        var svc = ClassService(db);
        var result = await svc.GetByIdAsync(_tenantA, identityUserId, session.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(session.Id, result.Data!.Id);
        Assert.Equal("Yoga Flow", result.Data.Name);
        Assert.Equal("يوغا", result.Data.NameAr);
        Assert.NotNull(result.Data.Trainer);
        Assert.Equal(coach.Id, result.Data.Trainer!.Id);
        Assert.Equal("Coach Sam", result.Data.Trainer.Name);
        Assert.Equal(10, result.Data.Availability.Capacity);
        Assert.Equal(10, result.Data.Availability.AvailableSeats);
        Assert.True(result.Data.BookingRequired);
    }

    [Fact]
    public async Task Available_Seats_Reflects_Active_Bookings()
    {
        var (db, _) = NewContext(_tenantA);
        var (activity, coach) = await SeedClassActivityAsync(db, _tenantA);
        var (identityUserId, member) = await SeedMemberWithIdentityAsync(db, _tenantA);
        var session = await SeedUpcomingSessionAsync(db, _tenantA, activity.Id, coach.Id, DateTime.UtcNow.AddHours(6), capacity: 5);

        db.ActivityBookings.Add(new ActivityBooking
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, SessionId = session.Id,
            MemberId = member.Id, Status = ActivityBookingStatuses.Booked, Source = "reception"
        });
        db.ActivityBookings.Add(new ActivityBooking
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, SessionId = session.Id,
            MemberId = Guid.NewGuid(), Status = ActivityBookingStatuses.CheckedIn, Source = "reception"
        });
        db.ActivityBookings.Add(new ActivityBooking
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, SessionId = session.Id,
            MemberId = Guid.NewGuid(), Status = ActivityBookingStatuses.Cancelled, Source = "reception"
        });
        await db.SaveChangesAsync();

        var svc = ClassService(db);
        var list = await svc.ListUpcomingAsync(_tenantA, identityUserId);
        var row = Assert.Single(list.Data!, r => r.Id == session.Id);
        Assert.Equal(2, row.BookedCount);
        Assert.Equal(3, row.AvailableSeats);

        var detail = await svc.GetByIdAsync(_tenantA, identityUserId, session.Id);
        Assert.True(detail.IsSuccess);
        Assert.Equal(2, detail.Data!.Availability.BookedCount);
        Assert.Equal(3, detail.Data.Availability.AvailableSeats);
    }

    [Fact]
    public async Task Past_And_Cancelled_Classes_Excluded_From_List()
    {
        var (db, _) = NewContext(_tenantA);
        var (activity, coach) = await SeedClassActivityAsync(db, _tenantA);
        var (identityUserId, _) = await SeedMemberWithIdentityAsync(db, _tenantA);

        await SeedUpcomingSessionAsync(db, _tenantA, activity.Id, coach.Id, DateTime.UtcNow.AddHours(-2));
        var upcomingSession = await SeedUpcomingSessionAsync(db, _tenantA, activity.Id, coach.Id, DateTime.UtcNow.AddHours(3));
        var cancelled = await SeedUpcomingSessionAsync(db, _tenantA, activity.Id, coach.Id, DateTime.UtcNow.AddHours(4));
        cancelled.Status = ActivitySessionStatuses.Cancelled;
        await db.SaveChangesAsync();

        var svc = ClassService(db);
        var result = await svc.ListUpcomingAsync(_tenantA, identityUserId);

        Assert.True(result.IsSuccess);
        var upcoming = Assert.Single(result.Data!, r => r.StartsAtUtc > DateTime.UtcNow);
        Assert.Equal(upcomingSession.Id, upcoming.Id);
    }

    [Fact]
    public async Task Past_Or_Cancelled_Class_Details_Return_Not_Found()
    {
        var (db, _) = NewContext(_tenantA);
        var (activity, coach) = await SeedClassActivityAsync(db, _tenantA);
        var (identityUserId, _) = await SeedMemberWithIdentityAsync(db, _tenantA);

        var past = await SeedUpcomingSessionAsync(db, _tenantA, activity.Id, coach.Id, DateTime.UtcNow.AddHours(-1));
        var cancelled = await SeedUpcomingSessionAsync(db, _tenantA, activity.Id, coach.Id, DateTime.UtcNow.AddHours(5));
        cancelled.Status = ActivitySessionStatuses.Cancelled;
        await db.SaveChangesAsync();

        var svc = ClassService(db);
        Assert.False((await svc.GetByIdAsync(_tenantA, identityUserId, past.Id)).IsSuccess);
        Assert.False((await svc.GetByIdAsync(_tenantA, identityUserId, cancelled.Id)).IsSuccess);
        Assert.False((await svc.GetByIdAsync(_tenantA, identityUserId, Guid.NewGuid())).IsSuccess);
    }

    [Fact]
    public async Task Unknown_Identity_Cannot_List_Classes()
    {
        var (db, _) = NewContext(_tenantA);
        await SeedClassActivityAsync(db, _tenantA);
        var svc = ClassService(db);

        var result = await svc.ListUpcomingAsync(_tenantA, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Facility_Activities_Are_Not_Returned_As_Classes()
    {
        var (db, _) = NewContext(_tenantA);
        var (identityUserId, _) = await SeedMemberWithIdentityAsync(db, _tenantA);
        db.Tenants.Add(new Tenant { Id = _tenantA, Name = "T", IsActive = true });
        var facility = new Activity
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, Name = "Gym Floor",
            Kind = ActivityKinds.Facility, VisibleToMembers = true, IsActive = true
        };
        db.Activities.Add(facility);
        await db.SaveChangesAsync();

        var starts = DateTime.UtcNow.AddHours(3);
        db.ActivitySessions.Add(new ActivitySession
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, ActivityId = facility.Id,
            StartsAtUtc = starts, EndsAtUtc = starts.AddHours(1), Capacity = 50,
            Status = ActivitySessionStatuses.Upcoming
        });
        await db.SaveChangesAsync();

        var svc = ClassService(db);
        var result = await svc.ListUpcomingAsync(_tenantA, identityUserId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task Browsing_Classes_Does_Not_Create_Bookings_Or_Payments()
    {
        var (db, _) = NewContext(_tenantA);
        var (activity, coach) = await SeedClassActivityAsync(db, _tenantA);
        var (identityUserId, _) = await SeedMemberWithIdentityAsync(db, _tenantA);
        var session = await SeedUpcomingSessionAsync(db, _tenantA, activity.Id, coach.Id, DateTime.UtcNow.AddHours(7));

        var bookingsBefore = await db.ActivityBookings.CountAsync();
        var paymentsBefore = await db.PaymentTransactions.CountAsync();
        var salesBefore = await db.Sales.CountAsync();

        var svc = ClassService(db);
        await svc.ListUpcomingAsync(_tenantA, identityUserId);
        await svc.GetByIdAsync(_tenantA, identityUserId, session.Id);
        await svc.ListUpcomingAsync(_tenantA, identityUserId);
        await svc.GetByIdAsync(_tenantA, identityUserId, session.Id);

        Assert.Equal(bookingsBefore, await db.ActivityBookings.CountAsync());
        Assert.Equal(paymentsBefore, await db.PaymentTransactions.CountAsync());
        Assert.Equal(salesBefore, await db.Sales.CountAsync());
    }

    [Fact]
    public async Task Tenant_A_Never_Sees_Tenant_B_Classes()
    {
        var tenantB = Guid.NewGuid();
        var (dbA, _) = NewContext(_tenantA);
        var (dbB, _) = NewContext(tenantB);

        var (activityA, coachA) = await SeedClassActivityAsync(dbA, _tenantA);
        var (identityA, _) = await SeedMemberWithIdentityAsync(dbA, _tenantA);
        var sessionA = await SeedUpcomingSessionAsync(dbA, _tenantA, activityA.Id, coachA.Id, DateTime.UtcNow.AddHours(2));

        var (activityB, coachB) = await SeedClassActivityAsync(dbB, tenantB);
        var (identityB, _) = await SeedMemberWithIdentityAsync(dbB, tenantB);
        var sessionB = await SeedUpcomingSessionAsync(dbB, tenantB, activityB.Id, coachB.Id, DateTime.UtcNow.AddHours(3));

        var listA = await ClassService(dbA).ListUpcomingAsync(_tenantA, identityA);
        Assert.True(listA.IsSuccess, listA.Error);
        Assert.Contains(listA.Data!, r => r.Id == sessionA.Id);
        Assert.DoesNotContain(listA.Data!, r => r.Id == sessionB.Id);

        var crossDetail = await ClassService(dbA).GetByIdAsync(_tenantA, identityA, sessionB.Id);
        Assert.False(crossDetail.IsSuccess);

        var listB = await ClassService(dbB).ListUpcomingAsync(tenantB, identityB);
        Assert.True(listB.IsSuccess, listB.Error);
        Assert.Contains(listB.Data!, r => r.Id == sessionB.Id);
        Assert.DoesNotContain(listB.Data!, r => r.Id == sessionA.Id);
    }
}
