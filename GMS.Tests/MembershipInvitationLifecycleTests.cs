namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Common;
using GMS.Application.DTOs.Invitation;
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
/// Refund → same-plan renew → Invitation must resolve the new covering membership,
/// not the refunded historical row.
/// </summary>
public class MembershipInvitationLifecycleTests
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

    private sealed class FakeEncryption : IEncryptionService
    {
        public string Encrypt(string plainText) => "enc:" + plainText;
        public string Decrypt(string cipherText)
            => cipherText.StartsWith("enc:", StringComparison.Ordinal) ? cipherText[4..] : cipherText;
    }

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<Result<PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(Result<PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static GymFlowProDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        return new GymFlowProDbContext(options, tenantContext);
    }

    [Fact]
    public async Task RefundThenRenewSamePlan_CreatesNewCoveringMembership_InvitationWorks()
    {
        var tenantId = Guid.NewGuid();
        var ctx = CreateContext(tenantId);
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");

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

        var requesterId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var requester = new AppUser
        {
            TenantId = tenantId,
            UserId = requesterId.ToString(),
            FirstName = "Desk",
            LastName = "One",
            Email = $"r-{requesterId:N}@test.local",
            Role = "Receptionist",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var owner = new AppUser
        {
            TenantId = tenantId,
            UserId = ownerId.ToString(),
            FirstName = "Owner",
            LastName = "User",
            Email = $"o-{ownerId:N}@test.local",
            Role = "Owner",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.AddRange(requester, owner);

        var memberIdentity = Guid.NewGuid();
        var memberAppUser = new AppUser
        {
            TenantId = tenantId,
            UserId = memberIdentity.ToString(),
            FirstName = "Member",
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
            MemberNumber = "M-100",
            FullName = "Member One",
            FullNameAr = "عضو",
            PhoneNumber = "+201011111111",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true,
            AppUserId = memberAppUser.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.GymMembers.Add(member);

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Monthly",
            NameAr = "شهري",
            PlanType = "monthly_unlimited",
            DurationDays = 30,
            Price = 500m,
            ReferralInviteQuota = 3,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.MembershipPlans.Add(plan);

        var today = MembershipOperational.TodayCairo();
        var membershipA = new Membership
        {
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = plan.Id,
            StartDate = today.AddDays(-5),
            EndDate = today.AddDays(25),
            Status = "active",
            AmountPaid = 500m,
            PaymentMethod = "cash",
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Memberships.Add(membershipA);

        var sale = new Sale
        {
            TenantId = tenantId,
            MemberId = member.Id,
            SoldByUserId = requester.Id,
            Subtotal = 500m,
            Total = 500m,
            Status = "completed"
        };
        ctx.Sales.Add(sale);
        ctx.SaleLines.Add(new SaleLine
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            LineType = "membership",
            ReferenceId = membershipA.Id,
            Description = plan.Name,
            Qty = 1,
            UnitPrice = 500m,
            LineTotal = 500m
        });
        await ctx.SaveChangesAsync();

        var invitations = new InvitationService(
            ctx, new FakeEncryption(), new NoOpAudit(), NullLogger<InvitationService>.Instance);

        var first = await invitations.SendInvitationForMemberAsync(new SendInvitationRequest
        {
            Name = "Friend One",
            PhoneNumber = "01012345001"
        }, member.Id, tenantId);
        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(membershipA.Id, (await ctx.MemberInvitations.SingleAsync()).CoveringMembershipId);

        var audit = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var shifts = new ShiftService(ctx, audit, NullLogger<ShiftService>.Instance);
        var refunds = new RefundService(
            ctx, shifts, new NoOpInvoiceService(), new NoOpWhatsApp(),
            new NoOpPaymob(), new NoOpFawry(), audit,
            new NoOpReferralRewardService(),
            new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance),
            NullLogger<RefundService>.Instance);

        var requested = await refunds.RequestAsync(sale.Id, 500m, "credit", "Full membership refund", requesterId, tenantId);
        Assert.True(requested.IsSuccess, requested.Error);
        var approved = await refunds.ApproveAsync(requested.Data!.Id, ownerId, tenantId);
        Assert.True(approved.IsSuccess, approved.Error);

        Assert.Equal("cancelled", (await ctx.Memberships.SingleAsync(m => m.Id == membershipA.Id)).Status);
        Assert.Equal("refunded", (await ctx.Sales.SingleAsync(s => s.Id == sale.Id)).Status);

        var afterRefund = await invitations.SendInvitationForMemberAsync(new SendInvitationRequest
        {
            Name = "Friend Two",
            PhoneNumber = "01012345002"
        }, member.Id, tenantId);
        Assert.False(afterRefund.IsSuccess);

        var memberships = new MembershipService(
            ctx,
            new Repository<Membership>(ctx),
            tenantContext,
            shifts,
            new NoOpInvoiceService(),
            audit,
            new NoOpReferralAttribution(),
            new ActivityEntitlementService(ctx),
            NullLogger<MembershipService>.Instance);

        var currentAfterRefund = await memberships.GetCurrentMembershipAsync(member.Id);
        Assert.True(currentAfterRefund.IsSuccess, currentAfterRefund.Error);
        Assert.Equal("cancelled", currentAfterRefund.Data!.Status);

        var renewed = await memberships.RenewMembershipAsync(
            tenantId, member.Id,
            new RenewMembershipRequest
            {
                PlanId = null,
                PaymentMethod = "cash",
                AmountPaid = 0m,
                TransitionMode = "cancel_and_switch"
            },
            ownerId);
        Assert.True(renewed.IsSuccess, renewed.Error);
        Assert.Equal("active", renewed.Data!.Status);
        Assert.NotEqual(membershipA.Id, (await ctx.Memberships.SingleAsync(m => m.Status == "active")).Id);

        var membershipB = await ctx.Memberships.SingleAsync(m => m.Status == "active");
        Assert.Equal(plan.Id, membershipB.PlanId);
        Assert.Equal("cancelled", (await ctx.Memberships.SingleAsync(m => m.Id == membershipA.Id)).Status);
        Assert.Equal("refunded", (await ctx.Sales.SingleAsync(s => s.Id == sale.Id)).Status);

        var renewSale = await ctx.Sales.SingleAsync(s => s.MemberId == member.Id && s.Id != sale.Id);
        Assert.Equal("partially_paid", renewSale.Status);
        Assert.Equal(500m, renewSale.Total);
        Assert.Equal(500m, renewSale.AmountDue);

        var afterRenew = await invitations.SendInvitationForMemberAsync(new SendInvitationRequest
        {
            Name = "Friend Three",
            PhoneNumber = "01012345003"
        }, member.Id, tenantId);
        Assert.True(afterRenew.IsSuccess, afterRenew.Error);

        var newInvite = await ctx.MemberInvitations.SingleAsync(i => i.GuestName == "Friend Three");
        Assert.Equal(membershipB.Id, newInvite.CoveringMembershipId);
        Assert.NotEqual(membershipA.Id, newInvite.CoveringMembershipId);

        var original = await ctx.MemberInvitations.SingleAsync(i => i.GuestName == "Friend One");
        Assert.Equal(membershipA.Id, original.CoveringMembershipId);
        Assert.Equal("new", original.Status);

        var quota = await invitations.GetMemberInvitation360Async(member.Id, tenantId);
        Assert.True(quota.IsSuccess, quota.Error);
        Assert.Equal(membershipB.Id, quota.Data!.Quota.MembershipId);
        Assert.Equal(1, quota.Data.Quota.Used);
        Assert.Equal(2, quota.Data.Quota.Remaining);
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

    private sealed class NoOpPaymob : IPaymobService
    {
        public Task<string> CreatePaymentIntentAsync(Guid membershipId, decimal amount, string memberPhone) =>
            Task.FromResult(string.Empty);
        public bool VerifyWebhookSignature(byte[] body, string hmacHeader) => true;
        public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);
    }

    private sealed class NoOpFawry : IFawryService
    {
        public Task<string> CreateOrderAsync(Guid membershipId, decimal amount) => Task.FromResult(string.Empty);
        public bool VerifyWebhookSignature(byte[] body, string signature) => true;
        public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);
    }
}
