namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Common;
using GMS.Application.DTOs.Invoices;
using GMS.Application.DTOs.Memberships;
using GMS.Application.DTOs.Plans;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Application.Validators;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;

/// <summary>
/// PRIVATE (pt_credits) membership plan type: CRUD, validation, membership creation/quota
/// initialization, PT session consumption (and over-consumption guard), and regression
/// coverage confirming existing plan types are unaffected.
/// </summary>
public class PrivateMembershipPlanTests
{
    private sealed class NoOpInvoiceService : IInvoiceService
    {
        public Task EnqueueForSale(Guid saleId) => Task.CompletedTask;
        public Task CreateForSaleAsync(Guid saleId) => Task.CompletedTask;
        public Task<Result<Guid>> CreateCreditNoteAsync(Guid refundId) =>
            Task.FromResult(Result<Guid>.Failure("not implemented in test double"));
        public Task<Result<PagedResult<InvoiceDto>>> GetPagedAsync(Guid tenantId, InvoiceQueryRequest query) =>
            Task.FromResult(Result<PagedResult<InvoiceDto>>.Failure("not implemented in test double"));
        public Task<Result<InvoiceDto>> GetByIdAsync(Guid id) =>
            Task.FromResult(Result<InvoiceDto>.Failure("not implemented in test double"));
        public Task<Result<bool>> ResendAsync(Guid invoiceId) => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> VoidAsync(Guid invoiceId, string reason, Guid voidedByUserId) =>
            Task.FromResult(Result<bool>.Success(true));
        public Task<Result<PaymentReceiptInfoDto>> GetPaymentInfoAsync(Guid paymentTransactionId) =>
            Task.FromResult(Result<PaymentReceiptInfoDto>.Failure("not implemented in test double"));
        public Task<Result<Guid>> GetOriginalInvoiceIdForSaleAsync(Guid saleId) =>
            Task.FromResult(Result<Guid>.Failure("not implemented in test double"));
    }

    private sealed class Harness
    {
        public required GymFlowProDbContext Ctx { get; init; }
        public required MembershipService Memberships { get; init; }
        public required MembershipPlanService Plans { get; init; }
        public required IShiftService Shifts { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid StaffIdentityId { get; init; }
        public required Guid MemberId { get; init; }
    }

    private static Harness CreateHarness()
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
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        var staffIdentityId = Guid.NewGuid();
        ctx.AppUsers.Add(new AppUser
        {
            TenantId = tenantId,
            UserId = staffIdentityId.ToString(),
            FirstName = "Owner",
            LastName = "User",
            Email = $"o-{staffIdentityId:N}@test.local",
            Role = "Owner",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        var memberAppUserId = Guid.NewGuid();
        var memberAppUser = new AppUser
        {
            TenantId = tenantId,
            UserId = memberAppUserId.ToString(),
            FirstName = "Private",
            LastName = "Client",
            Email = $"m-{memberAppUserId:N}@test.local",
            Role = "Member",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(memberAppUser);

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-PT1",
            FullName = "Private Client",
            FullNameAr = "عميل برايفت",
            PhoneNumber = "+201033333333",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true,
            AppUserId = memberAppUser.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.GymMembers.Add(member);
        ctx.SaveChanges();

        var audit = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var shifts = new ShiftService(ctx, audit, NullLogger<ShiftService>.Instance);
        var memberships = new MembershipService(
            ctx,
            new Repository<Membership>(ctx),
            tenantContext,
            shifts,
            new NoOpInvoiceService(),
            audit,
            new NoOpReferralAttribution(),
            new ActivityEntitlementService(ctx),
            new SaleAdjustmentService(ctx, audit),
            NullLogger<MembershipService>.Instance);
        var plans = new MembershipPlanService(
            ctx, new Repository<MembershipPlan>(ctx), tenantContext, NullLogger<MembershipPlanService>.Instance);

        return new Harness
        {
            Ctx = ctx,
            Memberships = memberships,
            Plans = plans,
            Shifts = shifts,
            TenantId = tenantId,
            StaffIdentityId = staffIdentityId,
            MemberId = member.Id
        };
    }

    private static CreatePlanRequest PrivatePlanRequest(
        int sessionCount = 12, int? durationMinutes = 60, int durationDays = 30, decimal price = 3000m) => new()
    {
        Name = "Private Training Package",
        NameAr = "باقة برايفت",
        PlanType = "pt_credits",
        Price = price,
        DurationDays = durationDays,
        SessionCount = sessionCount,
        PtSessionDurationMinutes = durationMinutes
    };

    // ===================== Plan CRUD =====================

    [Fact]
    public async Task CreatePrivatePlan_PersistsSessionCountAndDuration()
    {
        var h = CreateHarness();

        var result = await h.Plans.CreatePlanAsync(h.TenantId, PrivatePlanRequest());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("pt_credits", result.Data!.PlanType);
        Assert.Equal(12, result.Data.SessionCount);
        Assert.Equal(60, result.Data.PtSessionDurationMinutes);
        Assert.Equal(3000m, result.Data.Price);
        Assert.Equal(30, result.Data.DurationDays);
    }

    [Fact]
    public async Task GetPrivatePlan_ReturnsSessionFields()
    {
        var h = CreateHarness();
        var created = await h.Plans.CreatePlanAsync(h.TenantId, PrivatePlanRequest(sessionCount: 20, durationMinutes: 45));

        var fetched = await h.Plans.GetPlanByIdAsync(created.Data!.Id);

        Assert.True(fetched.IsSuccess, fetched.Error);
        Assert.Equal(20, fetched.Data!.SessionCount);
        Assert.Equal(45, fetched.Data.PtSessionDurationMinutes);
    }

    [Fact]
    public async Task UpdatePrivatePlan_PersistsChanges()
    {
        var h = CreateHarness();
        var created = await h.Plans.CreatePlanAsync(h.TenantId, PrivatePlanRequest());

        var update = new UpdatePlanRequest
        {
            Name = "Private Training Package",
            NameAr = "باقة برايفت",
            PlanType = "pt_credits",
            Price = 4500m,
            DurationDays = 60,
            SessionCount = 20,
            PtSessionDurationMinutes = 90
        };
        var updated = await h.Plans.UpdatePlanAsync(created.Data!.Id, update);

        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal(4500m, updated.Data!.Price);
        Assert.Equal(60, updated.Data.DurationDays);
        Assert.Equal(20, updated.Data.SessionCount);
        Assert.Equal(90, updated.Data.PtSessionDurationMinutes);
    }

    // ===================== Validation =====================

    [Fact]
    public void CreatePlanRequestValidator_PrivatePlan_RequiresPositiveSessionCount()
    {
        var validator = new CreatePlanRequestValidator();
        var request = PrivatePlanRequest();
        request.SessionCount = null;

        var result = validator.Validate(request);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePlanRequest.SessionCount));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(60)]
    [InlineData(90)]
    public void CreatePlanRequestValidator_PrivatePlan_AllowsValidSessionDurations(int minutes)
    {
        var validator = new CreatePlanRequestValidator();
        var request = PrivatePlanRequest(durationMinutes: minutes);

        var result = validator.Validate(request);

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreatePlanRequest.PtSessionDurationMinutes));
    }

    [Fact]
    public void CreatePlanRequestValidator_PrivatePlan_RejectsInvalidSessionDuration()
    {
        var validator = new CreatePlanRequestValidator();
        var request = PrivatePlanRequest(durationMinutes: 40);

        var result = validator.Validate(request);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePlanRequest.PtSessionDurationMinutes));
    }

    [Fact]
    public void CreatePlanRequestValidator_PrivatePlan_RejectsInvalidDuration()
    {
        var validator = new CreatePlanRequestValidator();
        var request = PrivatePlanRequest(durationDays: 0);

        var result = validator.Validate(request);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePlanRequest.DurationDays));
    }

    [Fact]
    public void CreatePlanRequestValidator_ExistingPlanTypes_StillValid()
    {
        // Regression: adding pt_credits rules must not affect other plan types' validation.
        var validator = new CreatePlanRequestValidator();
        var monthly = new CreatePlanRequest
        {
            Name = "Monthly",
            NameAr = "شهري",
            PlanType = "monthly_unlimited",
            Price = 500m,
            DurationDays = 30
        };

        var result = validator.Validate(monthly);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    // ===================== Membership creation / quota initialization =====================

    [Fact]
    public async Task AssignMembership_PrivatePlan_InitializesSessionsRemainingAndDates()
    {
        var h = CreateHarness();
        var plan = await h.Plans.CreatePlanAsync(h.TenantId, PrivatePlanRequest(sessionCount: 12, durationDays: 30, price: 3000m));
        await h.Shifts.OpenAsync(0m, h.StaffIdentityId, h.TenantId);

        var result = await h.Memberships.AssignMembershipAsync(
            h.TenantId, h.MemberId,
            new AssignMembershipRequest { PlanId = plan.Data!.Id, PaymentMethod = "cash", AmountPaid = 3000m },
            h.StaffIdentityId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("pt_credits", result.Data!.PlanType);
        Assert.Equal(12, result.Data.SessionsRemaining);
        Assert.Equal(MembershipOperational.TodayCairo(), result.Data.StartDate);
        Assert.Equal(MembershipOperational.TodayCairo().AddDays(30), result.Data.EndDate);
    }

    [Fact]
    public async Task AssignMembership_MonthlyPlan_SessionsRemainingStillNull_Regression()
    {
        var h = CreateHarness();
        var plan = await h.Plans.CreatePlanAsync(h.TenantId, new CreatePlanRequest
        {
            Name = "Monthly",
            NameAr = "شهري",
            PlanType = "monthly_unlimited",
            Price = 500m,
            DurationDays = 30
        });
        await h.Shifts.OpenAsync(0m, h.StaffIdentityId, h.TenantId);

        var result = await h.Memberships.AssignMembershipAsync(
            h.TenantId, h.MemberId,
            new AssignMembershipRequest { PlanId = plan.Data!.Id, PaymentMethod = "cash", AmountPaid = 500m },
            h.StaffIdentityId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Data!.SessionsRemaining);
    }

    // ===================== PT session consumption =====================

    private static async Task<Membership> SeedActiveMembershipAsync(
        Harness h, string planType, int? sessionsRemaining, DateOnly? startDate = null, DateOnly? endDate = null, string status = "active")
    {
        var today = MembershipOperational.TodayCairo();
        var plan = new MembershipPlan
        {
            TenantId = h.TenantId,
            Name = "Private Training Package",
            NameAr = "باقة برايفت",
            PlanType = planType,
            SessionCount = sessionsRemaining,
            DurationDays = 30,
            Price = 3000m,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        h.Ctx.MembershipPlans.Add(plan);

        var membership = new Membership
        {
            TenantId = h.TenantId,
            MemberId = h.MemberId,
            PlanId = plan.Id,
            StartDate = startDate ?? today.AddDays(-1),
            EndDate = endDate ?? today.AddDays(29),
            Status = status,
            SessionsRemaining = sessionsRemaining,
            PaymentMethod = "cash",
            AmountPaid = 3000m,
            CreatedAtUtc = DateTime.UtcNow
        };
        h.Ctx.Memberships.Add(membership);
        await h.Ctx.SaveChangesAsync();
        return membership;
    }

    [Fact]
    public async Task ConsumePrivateSession_DecrementsSessionsRemaining()
    {
        var h = CreateHarness();
        var membership = await SeedActiveMembershipAsync(h, "pt_credits", sessionsRemaining: 12);

        var result = await h.Memberships.ConsumePrivateSessionAsync(h.TenantId, h.MemberId, h.StaffIdentityId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(11, result.Data!.SessionsRemaining);
        Assert.Equal(11, (await h.Ctx.Memberships.FindAsync(membership.Id))!.SessionsRemaining);
    }

    [Fact]
    public async Task ConsumePrivateSession_PreventsOverConsumption()
    {
        var h = CreateHarness();
        await SeedActiveMembershipAsync(h, "pt_credits", sessionsRemaining: 1);

        var first = await h.Memberships.ConsumePrivateSessionAsync(h.TenantId, h.MemberId, h.StaffIdentityId);
        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(0, first.Data!.SessionsRemaining);

        var second = await h.Memberships.ConsumePrivateSessionAsync(h.TenantId, h.MemberId, h.StaffIdentityId);

        Assert.False(second.IsSuccess);
        Assert.Contains("No PT sessions remaining", second.Error);
    }

    [Fact]
    public async Task ConsumePrivateSession_RejectsNonPrivatePlan_Regression()
    {
        // A session_pack membership must never be touched by the PT-session consumption path —
        // it stays on the check-in-triggered decrement only (CheckinService).
        var h = CreateHarness();
        await SeedActiveMembershipAsync(h, "session_pack", sessionsRemaining: 10);

        var result = await h.Memberships.ConsumePrivateSessionAsync(h.TenantId, h.MemberId, h.StaffIdentityId);

        Assert.False(result.IsSuccess);
        Assert.Contains("not a PRIVATE", result.Error);
        Assert.Equal(10, (await h.Ctx.Memberships.SingleAsync()).SessionsRemaining);
    }

    [Fact]
    public async Task ConsumePrivateSession_ExpiredMembership_Fails()
    {
        var h = CreateHarness();
        var today = MembershipOperational.TodayCairo();
        await SeedActiveMembershipAsync(
            h, "pt_credits", sessionsRemaining: 5,
            startDate: today.AddDays(-40), endDate: today.AddDays(-10));

        var result = await h.Memberships.ConsumePrivateSessionAsync(h.TenantId, h.MemberId, h.StaffIdentityId);

        Assert.False(result.IsSuccess);
        Assert.Contains("No active membership", result.Error);
    }

    [Fact]
    public async Task ConsumePrivateSession_FrozenMembership_Fails()
    {
        var h = CreateHarness();
        await SeedActiveMembershipAsync(h, "pt_credits", sessionsRemaining: 5, status: "frozen");

        var result = await h.Memberships.ConsumePrivateSessionAsync(h.TenantId, h.MemberId, h.StaffIdentityId);

        Assert.False(result.IsSuccess);
        Assert.Contains("frozen", result.Error);
    }
}
