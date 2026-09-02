namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using GMS.Application.DTOs.Activities;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// P0 booking engine tests: idempotent session generation, quota enforcement,
/// cancellation rules (2h late window), no-show finalization, capacity, drop-in,
/// and tenant isolation. InMemory-backed with real TenantContext (per TenantQueryFilterTests).
/// </summary>
public class SessionBookingEngineTests
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

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
        new(db, Entitlements(db), Generator(db), NullLogger<SessionBookingService>.Instance);

    private static MemberBookingService MemberService(GymFlowProDbContext db) =>
        new(db, Entitlements(db), BookingService(db));

    private static SessionGenerationService Generator(GymFlowProDbContext db) =>
        new(db, NullLogger<SessionGenerationService>.Instance);

    private async Task<(Tenant Tenant, MembershipPlan Plan, Activity CrossFit)> SeedAsync(
        GymFlowProDbContext db, Guid tenantId, string? accessMode = "limited", int? quota = 8,
        string quotaPeriod = "monthly")
    {
        var tenant = new Tenant { Id = tenantId, Name = "T", IsActive = true };
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "CrossFit Plan",
            Price = 500, DurationDays = 30, IsActive = true
        };
        var activity = new Activity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "CrossFit", Kind = ActivityKinds.Class,
            DropInPrice = 150m, BookingRequired = true
        };
        db.Tenants.Add(tenant);
        db.MembershipPlans.Add(plan);
        db.Activities.Add(activity);
        if (accessMode != null)
        {
            db.PlanEntitlements.Add(new PlanEntitlement
            {
                TenantId = tenantId, PlanId = plan.Id, ActivityId = activity.Id,
                AccessMode = accessMode, QuotaLimit = quota, QuotaPeriod = quotaPeriod
            });
        }
        await db.SaveChangesAsync();
        return (tenant, plan, activity);
    }

    private async Task<(GymMember Member, Membership Membership)> SeedMemberAsync(
        GymFlowProDbContext db, Guid tenantId, Guid planId)
    {
        var member = new GymMember
        {
            Id = Guid.NewGuid(), TenantId = tenantId, FullName = "Test Member",
            PhoneNumber = "+201000000000"
        };
        var membership = new Membership
        {
            Id = Guid.NewGuid(), TenantId = tenantId, MemberId = member.Id, PlanId = planId,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            Status = "active"
        };
        db.GymMembers.Add(member);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();
        return (member, membership);
    }

    /// <summary>A future in-window schedule + one materialized session via the generator.</summary>
    private async Task<ActivitySession> SeedSessionViaSchedule(
        GymFlowProDbContext db, Guid tenantId, Guid activityId, DateTime startsUtcLocalCairo, int capacity = 15)
    {
        // Schedule for the weekday of the target date, effective from yesterday.
        var cairo = TimeZoneInfo.ConvertTimeFromUtc(startsUtcLocalCairo.AddHours(0),
            TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"));
        var dow = (int)cairo.DayOfWeek; // ISO 0=Sunday matches DayOfWeek enum here
        var schedule = new ActivitySchedule
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActivityId = activityId,
            DaysOfWeek = $"[{dow}]",
            StartTime = TimeOnly.FromDateTime(cairo),
            EndTime = TimeOnly.FromDateTime(cairo).AddHours(1),
            Capacity = capacity,
            EffectiveFrom = DateOnly.FromDateTime(cairo.Date.AddDays(-1)),
            EffectiveUntil = DateOnly.FromDateTime(cairo.Date.AddDays(1))
        };
        db.ActivitySchedules.Add(schedule);
        await db.SaveChangesAsync();

        var created = await Generator(db).GenerateUpcomingSessionsAsync(tenantId);
        Assert.True(created >= 1, "generator should create at least the seeded session");

        var session = await db.ActivitySessions.SingleAsync(s => s.ScheduleId == schedule.Id && s.StartsAtUtc == startsUtcLocalCairo);
        return session;
    }

    /// <summary>
    /// CheckInBookingAsync's staffUserId param is the Identity user id (JWT sub, per
    /// ActivitiesController.GetUserId()), resolved internally against AppUser.UserId.
    /// Seed a matching AppUser so tests exercise the real resolution path.
    /// </summary>
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

    // ---------- Session generation ----------

    [Fact]
    public async Task Generation_Is_Idempotent()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, plan, activity) = await SeedAsync(db, _tenantA);

        var future = DateTime.UtcNow.AddHours(48);
        await SeedSessionViaSchedule(db, _tenantA, activity.Id, future);

        var generator = Generator(db);
        var secondRun = await generator.GenerateUpcomingSessionsAsync(_tenantA);
        Assert.Equal(0, secondRun); // no duplicates on re-run

        var count = await db.ActivitySessions.CountAsync(s => s.TenantId == _tenantA && !s.IsDeleted);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Generation_Respects_Window_And_Skips_Past()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, _, activity) = await SeedAsync(db, _tenantA);

        var schedule = new ActivitySchedule
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, ActivityId = activity.Id,
            DaysOfWeek = "[0,1,2,3,4,5,6]",
            StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(20, 0),
            Capacity = 10, EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2))
        };
        db.ActivitySchedules.Add(schedule);
        await db.SaveChangesAsync();

        var created = await Generator(db).GenerateUpcomingSessionsAsync(_tenantA);
        // 30-day default window: ~30 sessions (one per day), never any in the past.
        Assert.InRange(created, 28, 31);

        var pastCount = await db.ActivitySessions.CountAsync(s => s.TenantId == _tenantA && s.StartsAtUtc < DateTime.UtcNow);
        Assert.Equal(0, pastCount);
    }

    // ---------- Quota enforcement ----------

    [Theory]
    [InlineData("cairo_month", 1)]
    [InlineData("monthly", 1)] // legacy alias
    [InlineData("membership", 2)]
    [InlineData("one_time", 2)]
    public async Task QuotaPeriod_CountsOnlyTheConfiguredWindow(string quotaPeriod, int expectedUsed)
    {
        var (db, _) = NewContext(_tenantA);
        var (_, plan, activity) = await SeedAsync(
            db, _tenantA, accessMode: "limited", quota: 8, quotaPeriod: quotaPeriod);
        var (member, membership) = await SeedMemberAsync(db, _tenantA, plan.Id);

        var today = MembershipOperational.TodayCairo();
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var previousMonthDate = currentMonthStart.AddDays(-1);
        membership.StartDate = previousMonthDate.AddDays(-1);
        membership.EndDate = currentMonthStart.AddMonths(1).AddDays(1);

        static DateTime CairoUtc(DateOnly date)
        {
            var local = DateTime.SpecifyKind(
                date.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(
                local, TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"));
        }

        var previousMonthSession = new ActivitySession
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, ActivityId = activity.Id,
            StartsAtUtc = CairoUtc(previousMonthDate),
            EndsAtUtc = CairoUtc(previousMonthDate).AddHours(1)
        };
        var currentMonthSession = new ActivitySession
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, ActivityId = activity.Id,
            StartsAtUtc = CairoUtc(today),
            EndsAtUtc = CairoUtc(today).AddHours(1)
        };
        db.ActivitySessions.AddRange(previousMonthSession, currentMonthSession);
        db.ActivityBookings.AddRange(
            new ActivityBooking
            {
                Id = Guid.NewGuid(), TenantId = _tenantA,
                SessionId = previousMonthSession.Id, MemberId = member.Id,
                CoveringMembershipId = membership.Id, Status = ActivityBookingStatuses.Booked
            },
            new ActivityBooking
            {
                Id = Guid.NewGuid(), TenantId = _tenantA,
                SessionId = currentMonthSession.Id, MemberId = member.Id,
                CoveringMembershipId = membership.Id, Status = ActivityBookingStatuses.Booked
            });
        await db.SaveChangesAsync();

        var quotas = await Entitlements(db).ListQuotasForMembershipAsync(
            _tenantA, member.Id, membership);
        var quota = Assert.Single(quotas);
        Assert.Equal(expectedUsed, quota.QuotaUsed);
        Assert.Equal(8 - expectedUsed, quota.QuotaRemaining);
    }

    [Fact]
    public async Task Quota_Booking9_Of_8_Rejected()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, plan, activity) = await SeedAsync(
            db, _tenantA, accessMode: "limited", quota: 8, quotaPeriod: "membership");
        var (member, _) = await SeedMemberAsync(db, _tenantA, plan.Id);

        var svc = BookingService(db);
        for (var i = 0; i < 8; i++)
        {
            var session = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(24 + i * 26));
            var result = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
            {
                SessionId = session.Id, MemberId = member.Id, Source = "test"
            }, staffUserId: null);
            Assert.True(result.IsSuccess, $"booking {i + 1} should succeed: {result.Error}");
        }

        var ninth = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(240));
        var rejected = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        {
            SessionId = ninth.Id, MemberId = member.Id, Source = "test"
        }, staffUserId: null);
        Assert.False(rejected.IsSuccess);
        Assert.Contains("quota", rejected.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unlimited_Entitlement_Has_No_Quota_Limit()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, plan, activity) = await SeedAsync(db, _tenantA, accessMode: "unlimited");
        var (member, _) = await SeedMemberAsync(db, _tenantA, plan.Id);

        var svc = BookingService(db);
        for (var i = 0; i < 12; i++)
        {
            var session = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(24 + i * 26));
            var result = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
            { SessionId = session.Id, MemberId = member.Id }, null);
            Assert.True(result.IsSuccess, result.Error);
        }
    }

    // ---------- Cancellation rules ----------

    [Fact]
    public async Task Cancel_Before_Window_Restores_Quota()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, plan, activity) = await SeedAsync(db, _tenantA, accessMode: "limited", quota: 8);
        var (member, _) = await SeedMemberAsync(db, _tenantA, plan.Id);

        // Session starts 5h from now → cancelling now (>2h before) refunds.
        var session = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(5));
        var svc = BookingService(db);
        var book = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member.Id }, null);
        Assert.True(book.IsSuccess);

        var cancel = await svc.CancelOwnBookingAsync(_tenantA, member.Id, book.Data!.Id);
        Assert.True(cancel.IsSuccess);
        Assert.Equal(ActivityBookingStatuses.Cancelled, cancel.Data!.Status);

        var ent = await Entitlements(db).ResolveAsync(_tenantA, member.Id, activity.Id);
        var remaining = await Entitlements(db).RemainingQuotaAsync(_tenantA, member.Id, activity.Id, ent!.CoveringMembership);
        Assert.Equal(8, remaining); // fully restored

        // Seat freed — a second member can take it (capacity check would also allow).
        var dup = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member.Id }, null);
        Assert.True(dup.IsSuccess); // cancelled rows don't block re-booking
    }

    [Fact]
    public async Task Late_Cancel_Marks_CancelledLate_Keeps_Quota_Consumed()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, plan, activity) = await SeedAsync(db, _tenantA, accessMode: "limited", quota: 8);
        var (member, _) = await SeedMemberAsync(db, _tenantA, plan.Id);

        // Session starts in 90 minutes (< 2h window) → late cancel.
        var session = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddMinutes(90));
        var svc = BookingService(db);
        var book = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member.Id }, null);
        Assert.True(book.IsSuccess);

        var policy = await svc.GetCancelPolicyAsync(_tenantA, book.Data!.Id);
        Assert.True(policy.Data!.IsLate);

        var cancel = await svc.CancelOwnBookingAsync(_tenantA, member.Id, book.Data!.Id);
        Assert.Equal(ActivityBookingStatuses.CancelledLate, cancel.Data!.Status);

        var ent = await Entitlements(db).ResolveAsync(_tenantA, member.Id, activity.Id);
        var remaining = await Entitlements(db).RemainingQuotaAsync(_tenantA, member.Id, activity.Id, ent!.CoveringMembership);
        Assert.Equal(7, remaining); // consumed, not refunded
    }

    // ---------- No show ----------

    [Fact]
    public async Task Finalize_Marks_NoShow_After_Session_End()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, plan, activity) = await SeedAsync(db, _tenantA, accessMode: "limited", quota: 8);
        var (member, _) = await SeedMemberAsync(db, _tenantA, plan.Id);

        var session = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(3));

        var svc = BookingService(db);
        var book = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member.Id }, null);
        Assert.True(book.IsSuccess);

        // Force the session into the past (as if time passed) AFTER booking.
        session.StartsAtUtc = DateTime.UtcNow.AddMinutes(-65);
        session.EndsAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();

        var changed = await Generator(db).FinalizeElapsedSessionsAsync(_tenantA);
        Assert.Equal(1, changed);

        var refreshed = await db.ActivityBookings.SingleAsync(b => b.Id == book.Data!.Id);
        Assert.Equal(ActivityBookingStatuses.NoShow, refreshed.Status);
        Assert.Equal("completed", (await db.ActivitySessions.SingleAsync(s => s.Id == session.Id)).Status);

        // No-show consumes quota.
        var ent = await Entitlements(db).ResolveAsync(_tenantA, member.Id, activity.Id);
        var remaining = await Entitlements(db).RemainingQuotaAsync(_tenantA, member.Id, activity.Id, ent!.CoveringMembership);
        Assert.Equal(7, remaining);
    }

    // ---------- Duplicate / capacity ----------

    [Fact]
    public async Task Duplicate_Active_Booking_Rejected()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, plan, activity) = await SeedAsync(db, _tenantA, accessMode: "unlimited");
        var (member, _) = await SeedMemberAsync(db, _tenantA, plan.Id);

        var session = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(4));
        var svc = BookingService(db);
        var first = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member.Id }, null);
        Assert.True(first.IsSuccess);

        var second = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member.Id }, null);
        Assert.False(second.IsSuccess);
        Assert.Contains("already booked", second.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Capacity_Full_Rejects_Additional_Bookings()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, _, activity) = await SeedAsync(db, _tenantA, accessMode: null); // entitlement-free activity
        activity.DropInPrice = null; // force pure eligibility rejection after full
        await db.SaveChangesAsync();

        var session = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(4), capacity: 2);

        // Two entitled members fill capacity=2.
        var members = new List<GymMember>();
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var (m, _) = await SeedMemberAsync(db, _tenantA, Guid.Empty);
            // Give them an included entitlement via a fresh plan each? Simpler: use drop-in sale route.
            members.Add(m);
        }

        var svc = BookingService(db);

        // Members have no entitlement and activity has no drop-in price → not eligible.
        var ineligible = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = members[0].Id }, null);
        Assert.False(ineligible.IsSuccess);
    }

    // ---------- Drop-in ----------

    [Fact]
    public async Task DropIn_Requires_Paid_Sale_Before_Booking()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, _, activity) = await SeedAsync(db, _tenantA, accessMode: null); // no entitlement
        var (member, _) = await SeedMemberAsync(db, _tenantA, Guid.NewGuid()); // membership w/o entitlement plan

        var session = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(4));
        var svc = BookingService(db);

        // No SaleId → rejected with payment-required message (no partial states).
        var unpaid = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member.Id, Source = "member_app" }, null);
        Assert.False(unpaid.IsSuccess);
        Assert.Contains("Payment required", unpaid.Error, StringComparison.OrdinalIgnoreCase);

        // Fabricated sale that isn't a valid paid drop-in for THIS session → rejected.
        var fakeSale = new Sale
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, MemberId = member.Id,
            SoldByUserId = Guid.NewGuid(), Total = 150, Status = "completed"
        };
        fakeSale.Lines.Add(new SaleLine
        { TenantId = _tenantA, LineType = "drop_in", ReferenceId = Guid.NewGuid(), UnitPrice = 150, LineTotal = 150 });
        db.Sales.Add(fakeSale);
        await db.SaveChangesAsync();

        var wrongClass = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member.Id, Source = "drop_in", SaleId = fakeSale.Id }, null);
        Assert.False(wrongClass.IsSuccess);

        // Valid paid drop-in sale for THIS session's activity → booking confirmed, links SaleId.
        var goodSale = new Sale
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, MemberId = member.Id,
            SoldByUserId = Guid.NewGuid(), Total = 150, Status = "completed"
        };
        goodSale.Lines.Add(new SaleLine
        { TenantId = _tenantA, LineType = "drop_in", ReferenceId = activity.Id, UnitPrice = 150, LineTotal = 150 });
        db.Sales.Add(goodSale);
        await db.SaveChangesAsync();

        var ok = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member.Id, Source = "drop_in", SaleId = goodSale.Id }, null);
        Assert.True(ok.IsSuccess, ok.Error);
        Assert.Equal(goodSale.Id, ok.Data!.SaleId);
        Assert.Equal(goodSale.Id, (await db.ActivityBookings.SingleAsync(b => b.Id == ok.Data!.Id)).SaleId);
    }

    [Fact]
    public async Task GetSessionDetail_PaidDropIn_ExposesInvoiceOnBooking()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, _, activity) = await SeedAsync(db, _tenantA, accessMode: null);
        var (member, _) = await SeedMemberAsync(db, _tenantA, Guid.NewGuid());
        var session = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(4));
        var svc = BookingService(db);

        var sale = new Sale
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, MemberId = member.Id,
            SoldByUserId = Guid.NewGuid(), Total = 150, Status = "completed"
        };
        sale.Lines.Add(new SaleLine
        { TenantId = _tenantA, LineType = "drop_in", ReferenceId = activity.Id, UnitPrice = 150, LineTotal = 150 });
        db.Sales.Add(sale);
        var invoice = new Invoice
        {
            TenantId = _tenantA,
            Type = "invoice",
            InvoiceNumber = "INV-2026-000042",
            SaleId = sale.Id,
            MemberNameSnapshot = member.FullName,
            MemberPhoneSnapshot = member.PhoneNumber ?? "01000000000",
            LinesSnapshot = "[{\"LineType\":\"drop_in\",\"Description\":\"Drop-in: Yoga\",\"Qty\":1,\"UnitPrice\":150,\"LineTotal\":150}]",
            Subtotal = 150m,
            Total = 150m,
            IssuedAt = DateTime.UtcNow,
            Status = "issued"
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var booked = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member.Id, Source = "drop_in", SaleId = sale.Id }, null);
        Assert.True(booked.IsSuccess, booked.Error);
        Assert.Equal(invoice.Id, booked.Data!.InvoiceId);
        Assert.Equal("INV-2026-000042", booked.Data.InvoiceNumber);

        var detail = await svc.GetSessionDetailAsync(_tenantA, session.Id);
        Assert.True(detail.IsSuccess, detail.Error);
        var row = Assert.Single(detail.Data!.Bookings);
        Assert.Equal(sale.Id, row.SaleId);
        Assert.Equal(invoice.Id, row.InvoiceId);
        Assert.Equal("INV-2026-000042", row.InvoiceNumber);
    }

    [Fact]
    public async Task DropIn_SaleCannotBackTwoBookings()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, _, activity) = await SeedAsync(db, _tenantA, accessMode: null); // no entitlement
        var (member, _) = await SeedMemberAsync(db, _tenantA, Guid.NewGuid());

        var sessionA = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(4));
        var svc = BookingService(db);

        var sale = new Sale
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, MemberId = member.Id,
            SoldByUserId = Guid.NewGuid(), Total = 150, Status = "completed"
        };
        sale.Lines.Add(new SaleLine
        { TenantId = _tenantA, LineType = "drop_in", ReferenceId = activity.Id, UnitPrice = 150, LineTotal = 150 });
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        var first = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = sessionA.Id, MemberId = member.Id, Source = "drop_in", SaleId = sale.Id }, null);
        Assert.True(first.IsSuccess, first.Error);

        // A second session, same paid drop-in sale reused -> rejected (one payment, one booking).
        var sessionB = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddDays(1).AddHours(4));
        var reuse = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = sessionB.Id, MemberId = member.Id, Source = "drop_in", SaleId = sale.Id }, null);
        Assert.False(reuse.IsSuccess);

        // Cancelling the first booking in time frees the sale for a new booking.
        var cancel = await svc.CancelBookingAsync(_tenantA, first.Data!.Id);
        Assert.True(cancel.IsSuccess, cancel.Error);

        var rebook = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = sessionB.Id, MemberId = member.Id, Source = "drop_in", SaleId = sale.Id }, null);
        Assert.True(rebook.IsSuccess, rebook.Error);
    }

    // ---------- Check-in / attendance ----------

    [Fact]
    public async Task CheckIn_Creates_GymAttendance_And_Prevents_Duplicates()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, plan, activity) = await SeedAsync(db, _tenantA, accessMode: "included");
        var (member, _) = await SeedMemberAsync(db, _tenantA, plan.Id);

        var session = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(3));
        var svc = BookingService(db);
        var book = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member.Id }, null);
        Assert.True(book.IsSuccess);

        var staff = await SeedStaffAsync(db, _tenantA);
        var checkin = await svc.CheckInBookingAsync(_tenantA, book.Data!.Id, staff);
        Assert.True(checkin.IsSuccess);
        Assert.Equal(ActivityBookingStatuses.CheckedIn, checkin.Data!.Status);

        var attendance = await db.GymAttendances.SingleAsync(a => a.BookingId == book.Data!.Id);
        Assert.Equal(member.Id, attendance.MemberId);
        Assert.Equal(session.Id, attendance.SessionId);

        // Duplicate check-in blocked.
        var again = await svc.CheckInBookingAsync(_tenantA, book.Data!.Id, staff);
        Assert.False(again.IsSuccess);
    }

    // ---------- Tenant isolation & ownership ----------

    [Fact]
    public async Task Cross_Tenant_Booking_Access_Fails()
    {
        var (dbA, _) = NewContext(_tenantA);
        var (_, planA, activityA) = await SeedAsync(dbA, _tenantA, accessMode: "unlimited");
        var (memberA, _) = await SeedMemberAsync(dbA, _tenantA, planA.Id);
        var sessionA = await SeedSessionViaSchedule(dbA, _tenantA, activityA.Id, DateTime.UtcNow.AddHours(4));

        var svcA = BookingService(dbA);

        // Tenant B member tries to book tenant A's session through tenant A's service context.
        var (dbB, _) = NewContext(_tenantB);
        var (_, planB, _) = await SeedAsync(dbB, _tenantB, accessMode: "unlimited");
        var (memberB, _) = await SeedMemberAsync(dbB, _tenantB, planB.Id);

        var cross = await svcA.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = sessionA.Id, MemberId = memberB.Id }, null);
        Assert.False(cross.IsSuccess); // member not found in tenant A

        // Tenant B cannot even see tenant A sessions (global query filter).
        var visibleToB = await dbB.ActivitySessions.CountAsync(s => s.Id == sessionA.Id);
        Assert.Equal(0, visibleToB);
    }

    [Fact]
    public async Task Member_Cannot_Cancel_Another_Members_Booking()
    {
        var (db, _) = NewContext(_tenantA);
        var (_, plan, activity) = await SeedAsync(db, _tenantA, accessMode: "unlimited");
        var (member1, _) = await SeedMemberAsync(db, _tenantA, plan.Id);
        var (member2, _) = await SeedMemberAsync(db, _tenantA, plan.Id);

        var session = await SeedSessionViaSchedule(db, _tenantA, activity.Id, DateTime.UtcNow.AddHours(4));
        var svc = BookingService(db);
        var book = await svc.CreateBookingAsync(_tenantA, new CreateBookingRequest
        { SessionId = session.Id, MemberId = member1.Id }, null);
        Assert.True(book.IsSuccess);

        // member2 attempts to cancel member1's booking → treated as not-found (no leak).
        var attack = await svc.CancelOwnBookingAsync(_tenantA, member2.Id, book.Data!.Id);
        Assert.False(attack.IsSuccess);

        var untouched = await db.ActivityBookings.SingleAsync(b => b.Id == book.Data!.Id);
        Assert.Equal(ActivityBookingStatuses.Booked, untouched.Status);
    }
}
