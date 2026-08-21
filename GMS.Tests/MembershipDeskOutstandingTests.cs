namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Common;
using GMS.Application.DTOs.Invoices;
using GMS.Application.DTOs.Memberships;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;

/// <summary>
/// Desk cash renew must store plan price as Sale.Total and remaining as AmountDue
/// so Member 360 Outstanding (GET /debtors?memberId=) can see it.
/// </summary>
public class MembershipDeskOutstandingTests
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

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<Result<PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(Result<PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private sealed class NoOpPaymob : IPaymobService
    {
        public Task<string> CreatePaymentIntentAsync(Guid membershipId, decimal amount, string memberPhone) =>
            Task.FromResult(string.Empty);
        public bool VerifyWebhookSignature(byte[] body, string hmacHeader) => true;
        public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);
    }

    private sealed class NoOpWhatsApp : IWhatsAppService
    {
        public Task SendExpiryReminderAsync(Guid memberId, int daysLeft) => Task.CompletedTask;
        public Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(Guid memberId, string discountCode) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode) => Task.CompletedTask;
        public Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime) => Task.CompletedTask;
        public Task SendClassReminderAsync(string phone, string className, DateTime startTime) => Task.CompletedTask;
        public Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate) => Task.CompletedTask;
        public Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry) => Task.CompletedTask;
        public Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr) => Task.CompletedTask;
        public Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters) => Task.CompletedTask;
    }

    private sealed class MemoryCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public required GymFlowProDbContext Ctx { get; init; }
        public required MembershipService Memberships { get; init; }
        public required DebtorsService Debtors { get; init; }
        public required IShiftService Shifts { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid OwnerId { get; init; }
        public required Guid MemberId { get; init; }
        public required Guid PlanId { get; init; }
    }

    private static Harness CreateHarness(decimal planPrice, bool seedCurrentMembership = true)
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

        var ownerId = Guid.NewGuid();
        ctx.AppUsers.Add(new AppUser
        {
            TenantId = tenantId,
            UserId = ownerId.ToString(),
            FirstName = "Owner",
            LastName = "User",
            Email = $"o-{ownerId:N}@test.local",
            Role = "Owner",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        var memberIdentity = Guid.NewGuid();
        var memberAppUser = new AppUser
        {
            TenantId = tenantId,
            UserId = memberIdentity.ToString(),
            FirstName = "Rajab",
            LastName = "One",
            Email = $"m-{memberIdentity:N}@test.local",
            Role = "Member",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(memberAppUser);

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-025",
            FullName = "Rajab",
            FullNameAr = "رجب",
            PhoneNumber = "+201022222222",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true,
            AppUserId = memberAppUser.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.GymMembers.Add(member);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Session Pack 20",
            NameAr = "باقة",
            PlanType = "session_pack",
            SessionCount = 20,
            DurationDays = 90,
            Price = planPrice,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.MembershipPlans.Add(plan);

        var today = MembershipOperational.TodayCairo();
        if (seedCurrentMembership)
        {
            ctx.Memberships.Add(new Membership
            {
                TenantId = tenantId,
                MemberId = member.Id,
                PlanId = plan.Id,
                StartDate = today.AddDays(-5),
                EndDate = today.AddDays(25),
                Status = "active",
                AmountPaid = planPrice,
                PaymentMethod = "cash",
                CreatedAtUtc = DateTime.UtcNow
            });
        }
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
            NullLogger<MembershipService>.Instance);
        var debtors = new DebtorsService(
            ctx, new MemoryCache(), new NoOpWhatsApp(), new NoOpPaymob(),
            NullLogger<DebtorsService>.Instance);

        return new Harness
        {
            Ctx = ctx,
            Memberships = memberships,
            Debtors = debtors,
            Shifts = shifts,
            TenantId = tenantId,
            OwnerId = ownerId,
            MemberId = member.Id,
            PlanId = plan.Id
        };
    }

    [Fact]
    public async Task CashRenew_PaidLessThanPlanPrice_AppearsInOutstanding()
    {
        var h = CreateHarness(800m);
        var opened = await h.Shifts.OpenAsync(0m, h.OwnerId, h.TenantId);
        Assert.True(opened.IsSuccess, opened.Error);

        var renewed = await h.Memberships.RenewMembershipAsync(
            h.TenantId, h.MemberId,
            new RenewMembershipRequest
            {
                PlanId = h.PlanId,
                PaymentMethod = "cash",
                AmountPaid = 200m,
                TransitionMode = "cancel_and_switch"
            },
            h.OwnerId);

        Assert.True(renewed.IsSuccess, renewed.Error);
        Assert.Equal("active", renewed.Data!.Status);

        var sale = await h.Ctx.Sales.SingleAsync(s => s.MemberId == h.MemberId);
        Assert.Equal("partially_paid", sale.Status);
        Assert.Equal(800m, sale.Total);
        Assert.Equal(600m, sale.AmountDue);
        Assert.NotNull(sale.DueDate);

        var payment = await h.Ctx.PaymentTransactions.SingleAsync(p => p.SaleId == sale.Id);
        Assert.Equal(200m, payment.Amount);

        var debtors = await h.Debtors.GetDebtorsPagedAsync(h.TenantId, page: 1, pageSize: 1, h.MemberId);
        Assert.True(debtors.IsSuccess, debtors.Error);
        var row = Assert.Single(debtors.Data!.Items);
        Assert.Equal(600m, row.TotalDue);
    }

    [Fact]
    public async Task CashRenew_PaidFullPlanPrice_NotInOutstanding()
    {
        var h = CreateHarness(300m);
        var opened = await h.Shifts.OpenAsync(0m, h.OwnerId, h.TenantId);
        Assert.True(opened.IsSuccess, opened.Error);

        var renewed = await h.Memberships.RenewMembershipAsync(
            h.TenantId, h.MemberId,
            new RenewMembershipRequest
            {
                PlanId = h.PlanId,
                PaymentMethod = "cash",
                AmountPaid = 300m,
                TransitionMode = "cancel_and_switch"
            },
            h.OwnerId);

        Assert.True(renewed.IsSuccess, renewed.Error);

        var sale = await h.Ctx.Sales.SingleAsync(s => s.MemberId == h.MemberId);
        Assert.Equal("completed", sale.Status);
        Assert.Equal(300m, sale.Total);
        Assert.Equal(0m, sale.AmountDue);

        var debtors = await h.Debtors.GetDebtorsPagedAsync(h.TenantId, page: 1, pageSize: 1, h.MemberId);
        Assert.True(debtors.IsSuccess, debtors.Error);
        Assert.Empty(debtors.Data!.Items);
    }

    [Fact]
    public async Task CashAssign_PaidLessThanPlanPrice_AppearsInOutstanding()
    {
        var h = CreateHarness(300m, seedCurrentMembership: false);
        var opened = await h.Shifts.OpenAsync(0m, h.OwnerId, h.TenantId);
        Assert.True(opened.IsSuccess, opened.Error);

        var assigned = await h.Memberships.AssignMembershipAsync(
            h.TenantId, h.MemberId,
            new AssignMembershipRequest
            {
                PlanId = h.PlanId,
                PaymentMethod = "cash",
                AmountPaid = 100m
            },
            h.OwnerId);

        Assert.True(assigned.IsSuccess, assigned.Error);
        Assert.Equal("active", assigned.Data!.Status);
        Assert.Equal(100m, assigned.Data.AmountPaid);

        var sale = await h.Ctx.Sales.SingleAsync(s => s.MemberId == h.MemberId);
        Assert.Equal("partially_paid", sale.Status);
        Assert.Equal(300m, sale.Total);
        Assert.Equal(200m, sale.AmountDue);

        var payment = await h.Ctx.PaymentTransactions.SingleAsync(p => p.SaleId == sale.Id);
        Assert.Equal(100m, payment.Amount);

        var debtors = await h.Debtors.GetDebtorsPagedAsync(h.TenantId, page: 1, pageSize: 1, h.MemberId);
        Assert.True(debtors.IsSuccess, debtors.Error);
        var row = Assert.Single(debtors.Data!.Items);
        Assert.Equal(200m, row.TotalDue);
    }

    [Fact]
    public async Task CashAssign_OmittedAmountPaid_ChargesFullPlan_NotInOutstanding()
    {
        var h = CreateHarness(300m, seedCurrentMembership: false);
        var opened = await h.Shifts.OpenAsync(0m, h.OwnerId, h.TenantId);
        Assert.True(opened.IsSuccess, opened.Error);

        var assigned = await h.Memberships.AssignMembershipAsync(
            h.TenantId, h.MemberId,
            new AssignMembershipRequest
            {
                PlanId = h.PlanId,
                PaymentMethod = "cash"
            },
            h.OwnerId);

        Assert.True(assigned.IsSuccess, assigned.Error);
        Assert.Equal(300m, assigned.Data!.AmountPaid);

        var sale = await h.Ctx.Sales.SingleAsync(s => s.MemberId == h.MemberId);
        Assert.Equal("completed", sale.Status);
        Assert.Equal(300m, sale.Total);
        Assert.Equal(0m, sale.AmountDue);

        var debtors = await h.Debtors.GetDebtorsPagedAsync(h.TenantId, page: 1, pageSize: 1, h.MemberId);
        Assert.True(debtors.IsSuccess, debtors.Error);
        Assert.Empty(debtors.Data!.Items);
    }

    [Fact]
    public async Task CashAssign_PartialThenCancel_StopsOutstanding_NotARefund()
    {
        var h = CreateHarness(300m, seedCurrentMembership: false);
        var opened = await h.Shifts.OpenAsync(0m, h.OwnerId, h.TenantId);
        Assert.True(opened.IsSuccess, opened.Error);

        var assigned = await h.Memberships.AssignMembershipAsync(
            h.TenantId, h.MemberId,
            new AssignMembershipRequest
            {
                PlanId = h.PlanId,
                PaymentMethod = "cash",
                AmountPaid = 100m
            },
            h.OwnerId);
        Assert.True(assigned.IsSuccess, assigned.Error);

        var cancelled = await h.Memberships.CancelMembershipAsync(h.TenantId, h.MemberId, h.OwnerId);
        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal("cancelled", cancelled.Data!.Status);

        var sale = await h.Ctx.Sales.SingleAsync(s => s.MemberId == h.MemberId);
        Assert.Equal("partially_paid", sale.Status);
        Assert.Equal(0m, sale.AmountDue);
        Assert.Null(sale.DueDate);
        Assert.Empty(h.Ctx.Refunds);

        var member = await h.Ctx.GymMembers.SingleAsync(m => m.Id == h.MemberId);
        Assert.True(member.IsActive);
        Assert.Equal(0, member.InvitationQuotaRemaining);

        var debtors = await h.Debtors.GetDebtorsPagedAsync(h.TenantId, page: 1, pageSize: 1, h.MemberId);
        Assert.True(debtors.IsSuccess, debtors.Error);
        Assert.Empty(debtors.Data!.Items);
    }

    [Fact]
    public async Task PendingAssign_Cancel_StopsPlan_NoSale()
    {
        var h = CreateHarness(300m, seedCurrentMembership: false);
        var assigned = await h.Memberships.AssignMembershipAsync(
            h.TenantId, h.MemberId,
            new AssignMembershipRequest
            {
                PlanId = h.PlanId,
                PaymentMethod = "fawry"
            },
            h.OwnerId);
        Assert.True(assigned.IsSuccess, assigned.Error);
        Assert.Equal("pending", assigned.Data!.Status);

        var cancelled = await h.Memberships.CancelMembershipAsync(h.TenantId, h.MemberId, h.OwnerId);
        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal("cancelled", cancelled.Data!.Status);
        Assert.Empty(h.Ctx.Sales);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_Fails()
    {
        var h = CreateHarness(300m, seedCurrentMembership: false);
        var opened = await h.Shifts.OpenAsync(0m, h.OwnerId, h.TenantId);
        Assert.True(opened.IsSuccess, opened.Error);

        var assigned = await h.Memberships.AssignMembershipAsync(
            h.TenantId, h.MemberId,
            new AssignMembershipRequest { PlanId = h.PlanId, PaymentMethod = "cash" },
            h.OwnerId);
        Assert.True(assigned.IsSuccess, assigned.Error);

        var first = await h.Memberships.CancelMembershipAsync(h.TenantId, h.MemberId, h.OwnerId);
        Assert.True(first.IsSuccess, first.Error);

        var second = await h.Memberships.CancelMembershipAsync(h.TenantId, h.MemberId, h.OwnerId);
        Assert.False(second.IsSuccess);
        Assert.Contains("cannot be cancelled", second.Error, StringComparison.OrdinalIgnoreCase);
    }
}
