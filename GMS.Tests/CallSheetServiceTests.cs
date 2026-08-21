namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.CallSheet;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class CallSheetServiceTests
{
    private static (GymFlowProDbContext ctx, CallSheetService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var svc = new CallSheetService(ctx, NullLogger<CallSheetService>.Instance);

        return (ctx, svc, tenantId);
    }

    private static AppUser SeedStaff(GymFlowProDbContext ctx, Guid tenantId)
    {
        var staff = new AppUser
        {
            TenantId = tenantId,
            UserId = Guid.NewGuid().ToString(),
            FirstName = "Front",
            LastName = "Desk",
            Email = $"staff-{Guid.NewGuid()}@test.local",
            Role = "Receptionist"
        };
        ctx.AppUsers.Add(staff);
        return staff;
    }

    private static GymMember SeedMember(GymFlowProDbContext ctx, Guid tenantId, string name = "Ahmed Ali")
    {
        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-024",
            FullName = name,
            FullNameAr = "أحمد",
            PhoneNumber = "01012345678",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true
        };
        ctx.GymMembers.Add(member);
        return member;
    }

    private static MembershipPlan SeedPlan(GymFlowProDbContext ctx, Guid tenantId, string type = "monthly_unlimited")
    {
        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Gold Monthly",
            NameAr = "ذهبي",
            PlanType = type,
            DurationDays = 30,
            Price = 800m,
            IsActive = true
        };
        ctx.MembershipPlans.Add(plan);
        return plan;
    }

    private static Membership SeedMembership(
        GymFlowProDbContext ctx, Guid tenantId, Guid memberId, Guid planId, DateOnly start, DateOnly end, string status = "active")
    {
        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = memberId,
            PlanId = planId,
            StartDate = start,
            EndDate = end,
            Status = status
        };
        ctx.Memberships.Add(membership);
        return membership;
    }

    [Fact]
    public async Task GetRenewalRateAsync_TenOutcomesThreeRenewed_Returns30Percent()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var staff = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var outcomes = new[] { "renewed", "renewed", "renewed", "contacted", "contacted", "declined", "declined", "no_answer", "no_answer", "contacted" };

        foreach (var outcome in outcomes)
        {
            var member = SeedMember(ctx, tenantId, "M " + Guid.NewGuid().ToString("N")[..6]);
            var plan = SeedPlan(ctx, tenantId);
            var membership = SeedMembership(ctx, tenantId, member.Id, plan.Id,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                DateOnly.FromDateTime(DateTime.UtcNow));
            ctx.CallOutcomes.Add(new CallOutcome
            {
                TenantId = tenantId,
                MembershipId = membership.Id,
                MemberId = member.Id,
                UserId = staff.Id,
                Outcome = outcome
            });
        }
        await ctx.SaveChangesAsync();

        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var result = await svc.GetRenewalRateAsync(tenantId, from, to, staffUserId: null);

        Assert.True(result.IsSuccess, result.Error);
        var row = Assert.Single(result.Data!);
        Assert.Equal(staff.Id, row.StaffUserId);
        Assert.Equal(10, row.TotalCalled);
        Assert.Equal(3, row.Renewed);
        Assert.Equal(30.00m, row.RenewalRatePercent);
    }

    [Fact]
    public async Task GetQueueAsync_ExpiringMembership_CreatesOneRenewalFollowUp()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var today = MembershipOperational.TodayCairo();
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId);
        SeedMembership(ctx, tenantId, member.Id, plan.Id, today.AddDays(-27), today.AddDays(3));
        await ctx.SaveChangesAsync();

        var first = await svc.GetQueueAsync(tenantId, null, "today", null, null, null, null, null);
        Assert.True(first.IsSuccess, first.Error);
        var renewal = Assert.Single(first.Data!.Items.Where(i => i.Reason == "renewal" && i.Status != "completed"));
        Assert.Equal("system", renewal.Source);
        Assert.Contains("expires in 3 day", renewal.Why, StringComparison.OrdinalIgnoreCase);

        var second = await svc.GetQueueAsync(tenantId, null, "today", null, null, null, null, null);
        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(1, second.Data!.Items.Count(i => i.Reason == "renewal" && i.Id == renewal.Id));
    }

    [Fact]
    public async Task RecordOutcome_ReachedWillVisit_SetsContactedNotCompleted()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var staff = SeedStaff(ctx, tenantId);
        var today = MembershipOperational.TodayCairo();
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId);
        SeedMembership(ctx, tenantId, member.Id, plan.Id, today.AddDays(-27), today.AddDays(2));
        await ctx.SaveChangesAsync();

        var queue = await svc.GetQueueAsync(tenantId, null, "today", null, null, null, null, null);
        var follow = Assert.Single(queue.Data!.Items.Where(i => i.Reason == "renewal"));

        var recorded = await svc.RecordOutcomeAsync(follow.Id, tenantId, Guid.Parse(staff.UserId), new RecordCallOutcomeRequest
        {
            Outcome = "reached",
            Note = "Will visit tomorrow",
            NextAction = "member_will_visit"
        });
        Assert.True(recorded.IsSuccess, recorded.Error);

        var detail = await svc.GetByIdAsync(follow.Id, tenantId);
        Assert.True(detail.IsSuccess, detail.Error);
        Assert.Equal("contacted", detail.Data!.Status);
        Assert.Equal("reached", detail.Data.LastOutcome);
        Assert.Equal("member_will_visit", detail.Data.NextAction);
        Assert.NotEqual("completed", detail.Data.Status);
        Assert.Single(detail.Data.History);
    }

    [Fact]
    public async Task RecordOutcome_Renewed_CompletesFollowUp()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var staff = SeedStaff(ctx, tenantId);
        var today = MembershipOperational.TodayCairo();
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId);
        SeedMembership(ctx, tenantId, member.Id, plan.Id, today.AddDays(-27), today.AddDays(1));
        await ctx.SaveChangesAsync();

        var queue = await svc.GetQueueAsync(tenantId, null, "today", null, null, null, null, null);
        var follow = Assert.Single(queue.Data!.Items.Where(i => i.Reason == "renewal"));

        var recorded = await svc.RecordOutcomeAsync(follow.Id, tenantId, Guid.Parse(staff.UserId), new RecordCallOutcomeRequest
        {
            Outcome = "renewed",
            NextAction = "member_renewed"
        });
        Assert.True(recorded.IsSuccess, recorded.Error);

        var detail = await svc.GetByIdAsync(follow.Id, tenantId);
        Assert.Equal("completed", detail.Data!.Status);
    }

    [Fact]
    public async Task Sync_DoesNotRecreateCompletedRenewalForSameMembership()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var staff = SeedStaff(ctx, tenantId);
        var today = MembershipOperational.TodayCairo();
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId);
        SeedMembership(ctx, tenantId, member.Id, plan.Id, today.AddDays(-27), today.AddDays(3));
        await ctx.SaveChangesAsync();

        var queue = await svc.GetQueueAsync(tenantId, null, "today", null, null, null, null, null);
        var follow = Assert.Single(queue.Data!.Items.Where(i => i.Reason == "renewal"));
        await svc.CompleteAsync(follow.Id, tenantId, Guid.Parse(staff.UserId), "done");

        var again = await svc.GetQueueAsync(tenantId, null, "all", null, null, null, null, null);
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(1, again.Data!.Items.Count(i => i.Reason == "renewal"));
        Assert.Equal("completed", again.Data.Items.Single(i => i.Reason == "renewal").Status);
    }

    [Fact]
    public async Task OutstandingSale_CreatesPaymentFollowUp()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var member = SeedMember(ctx, tenantId);
        ctx.Sales.Add(new Sale
        {
            TenantId = tenantId,
            MemberId = member.Id,
            SoldByUserId = Guid.NewGuid(),
            Status = "partially_paid",
            AmountDue = 600m,
            Total = 800m,
            Subtotal = 800m
        });
        await ctx.SaveChangesAsync();

        var queue = await svc.GetQueueAsync(tenantId, null, "today", "payment", null, null, null, null);
        Assert.True(queue.IsSuccess, queue.Error);
        var row = Assert.Single(queue.Data!.Items);
        Assert.Equal("payment", row.Reason);
        Assert.Equal("high", row.Priority);
        Assert.Contains("600", row.Why);
    }

    [Fact]
    public async Task CreateManual_DoesNotUseSystemSourceKey()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var staff = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var created = await svc.CreateAsync(tenantId, Guid.Parse(staff.UserId), new CreateFollowUpRequest
        {
            MemberId = member.Id,
            Reason = "custom",
            Priority = "medium",
            Why = "Asked us to call Friday"
        });
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal("manual", created.Data!.Source);
        Assert.StartsWith("manual:", (await ctx.MemberFollowUps.FindAsync(created.Data.Id))!.SourceKey);
    }
}
