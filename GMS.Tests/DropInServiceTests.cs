namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Regression coverage for DropInService.PurchaseDropInAsync's idempotent-reuse logic.
/// The original check compared SaleLine.ReferenceId against the session id, but the line it
/// creates stores the activity id — the two never matched, so retrying a purchase (double
/// click, network retry) silently created a second charge. Fixing the field mismatch alone
/// would introduce a worse bug: reusing an *already-consumed* sale forever, which would block
/// a returning drop-in customer from ever buying a second visit.
/// </summary>
public class DropInServiceTests
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

    private static DropInService Service(GymFlowProDbContext db) => new(db, NullLogger<DropInService>.Instance);
    private static ActivityEntitlementService Entitlements(GymFlowProDbContext db) => new(db);
    private static SessionBookingService BookingService(GymFlowProDbContext db) =>
        new(db, Entitlements(db), new SessionGenerationService(db, NullLogger<SessionGenerationService>.Instance), NullLogger<SessionBookingService>.Instance);
    private static SessionGenerationService Generator(GymFlowProDbContext db) =>
        new(db, NullLogger<SessionGenerationService>.Instance);

    private async Task<Guid> SeedStaffAsync(GymFlowProDbContext db, Guid tenantId)
    {
        var identityUserId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = identityUserId.ToString(),
            FirstName = "Front", LastName = "Desk", Email = $"staff-{Guid.NewGuid()}@test.local",
            Role = "Receptionist"
        });
        await db.SaveChangesAsync();
        return identityUserId;
    }

    private async Task<(GymMember Member, Activity Activity)> SeedMemberAndActivityAsync(GymFlowProDbContext db, Guid tenantId)
    {
        var activity = new Activity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Yoga", Kind = Core.Constants.ActivityKinds.Class,
            DropInPrice = 150m, BookingRequired = true
        };
        var member = new GymMember
        {
            Id = Guid.NewGuid(), TenantId = tenantId, FullName = "Drop-in Customer", PhoneNumber = "+201000000001"
        };
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "T", IsActive = true });
        db.Activities.Add(activity);
        db.GymMembers.Add(member);
        db.Shifts.Add(new Shift { Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(), OpeningFloat = 0, OpenedAt = DateTime.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();
        return (member, activity);
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
    public async Task Retrying_Purchase_For_Same_Unconsumed_Session_Reuses_The_Sale()
    {
        var (db, _) = NewContext(_tenantA);
        var (member, activity) = await SeedMemberAndActivityAsync(db, _tenantA);
        var session = await SeedSessionAsync(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(4));
        var staff = await SeedStaffAsync(db, _tenantA);
        var svc = Service(db);

        var first = await svc.PurchaseDropInAsync(_tenantA, member.Id, session.Id, staff, gateway: "cash");
        Assert.True(first.IsSuccess, first.Error);

        // Double-click / retry before the booking is confirmed -> same sale, no second charge.
        var retry = await svc.PurchaseDropInAsync(_tenantA, member.Id, session.Id, staff, gateway: "cash");
        Assert.True(retry.IsSuccess, retry.Error);
        Assert.Equal(first.Data, retry.Data);
        Assert.Equal(1, await db.Sales.CountAsync(s => s.TenantId == _tenantA));
    }

    [Fact]
    public async Task Purchase_After_Prior_DropIn_Consumed_Creates_A_New_Sale_Not_A_Reused_Stale_One()
    {
        var (db, _) = NewContext(_tenantA);
        var (member, activity) = await SeedMemberAndActivityAsync(db, _tenantA);
        var sessionA = await SeedSessionAsync(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(4));
        var staff = await SeedStaffAsync(db, _tenantA);
        var svc = Service(db);
        var bookingSvc = BookingService(db);

        var firstSale = await svc.PurchaseDropInAsync(_tenantA, member.Id, sessionA.Id, staff, gateway: "cash");
        Assert.True(firstSale.IsSuccess, firstSale.Error);
        var firstBooking = await bookingSvc.CreateBookingAsync(_tenantA, new GMS.Application.DTOs.Activities.CreateBookingRequest
        { SessionId = sessionA.Id, MemberId = member.Id, SaleId = firstSale.Data, Source = "drop_in" }, staff);
        Assert.True(firstBooking.IsSuccess, firstBooking.Error);

        // Returning customer, a different day's session of the same activity: must get a fresh
        // sale, not the already-consumed one from their last visit.
        var sessionB = await SeedSessionAsync(db, _tenantA, activity.Id, DateTime.UtcNow.AddDays(1).AddHours(4));
        var secondSale = await svc.PurchaseDropInAsync(_tenantA, member.Id, sessionB.Id, staff, gateway: "cash");
        Assert.True(secondSale.IsSuccess, secondSale.Error);
        Assert.NotEqual(firstSale.Data, secondSale.Data);

        var secondBooking = await bookingSvc.CreateBookingAsync(_tenantA, new GMS.Application.DTOs.Activities.CreateBookingRequest
        { SessionId = sessionB.Id, MemberId = member.Id, SaleId = secondSale.Data, Source = "drop_in" }, staff);
        Assert.True(secondBooking.IsSuccess, secondBooking.Error);
    }

    [Fact]
    public async Task Guest_DropIn_Creates_GuestSale_And_Booking()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, activity) = await SeedMemberAndActivityAsync(db, _tenantA);
        var session = await SeedSessionAsync(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(4));
        var staff = await SeedStaffAsync(db, _tenantA);

        var purchase = await Service(db).PurchaseDropInAsync(
            _tenantA, null, "  Walk-in Guest  ", "  +201000000099  ", session.Id, staff,
            paymentMethod: "card_paymob");

        Assert.True(purchase.IsSuccess, purchase.Error);
        var sale = await db.Sales.SingleAsync(s => s.Id == purchase.Data);
        Assert.Null(sale.MemberId);
        Assert.Equal("Walk-in Guest", sale.GuestName);
        Assert.Equal("+201000000099", sale.GuestPhone);
        Assert.Equal(1, await db.PaymentTransactions.CountAsync(p => p.SaleId == sale.Id));

        var booking = await BookingService(db).CreateBookingAsync(_tenantA,
            new GMS.Application.DTOs.Activities.CreateBookingRequest
            {
                SessionId = session.Id,
                GuestName = "Walk-in Guest",
                GuestPhone = "+201000000099",
                SaleId = sale.Id,
                Source = "guest_walk_in"
            }, staff);

        Assert.True(booking.IsSuccess, booking.Error);
        Assert.Null(booking.Data!.MemberId);
        Assert.Equal("Walk-in Guest", booking.Data.GuestName);
    }

    [Fact]
    public async Task Guest_DropIn_Rejects_Missing_Identity_And_Account_Credit()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, activity) = await SeedMemberAndActivityAsync(db, _tenantA);
        var session = await SeedSessionAsync(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(4));
        var staff = await SeedStaffAsync(db, _tenantA);
        var svc = Service(db);

        var missingIdentity = await svc.PurchaseDropInAsync(
            _tenantA, null, "", "+201000000099", session.Id, staff);
        var accountCredit = await svc.PurchaseDropInAsync(
            _tenantA, null, "Guest", "+201000000099", session.Id, staff,
            paymentMethod: "account_credit");

        Assert.False(missingIdentity.IsSuccess);
        Assert.False(accountCredit.IsSuccess);
        Assert.Empty(await db.Sales.ToListAsync());
    }
}
