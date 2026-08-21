namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Common;
using GMS.Application.DTOs.Invoices;
using GMS.Application.DTOs.Sales;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;
using GMS.Tests.Helpers;

public class RefundServiceTests
{
    private class NoOpInvoiceService : IInvoiceService
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

    private class RecordingInvoiceService : IInvoiceService
    {
        public List<Guid> CreateCreditNoteCalls { get; } = new();

        public Task EnqueueForSale(Guid saleId) => Task.CompletedTask;
        public Task CreateForSaleAsync(Guid saleId) => Task.CompletedTask;
        public Task<Result<Guid>> CreateCreditNoteAsync(Guid refundId)
        {
            CreateCreditNoteCalls.Add(refundId);
            return Task.FromResult(Result<Guid>.Success(Guid.NewGuid()));
        }
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

    private class NoOpWhatsAppService : IWhatsAppService
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

    private class NoOpPaymobService : IPaymobService
    {
        public Task<string> CreatePaymentIntentAsync(Guid membershipId, decimal amount, string memberPhone) =>
            Task.FromResult(string.Empty);
        public bool VerifyWebhookSignature(byte[] body, string hmacHeader) => true;
        public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);
    }

    private class NoOpFawryService : IFawryService
    {
        public Task<string> CreateOrderAsync(Guid membershipId, decimal amount) => Task.FromResult(string.Empty);
        public bool VerifyWebhookSignature(byte[] body, string signature) => true;
        public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);
    }

    private const string LocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;";

    private static (GymFlowProDbContext ctx, RefundService svc, Guid tenantId) CreateSut(
        bool useLocalDb = false, Func<GymFlowProDbContext, Guid, IInvoiceService>? invoiceServiceFactory = null)
    {
        var tenantId = Guid.NewGuid();

        var options = useLocalDb
            ? new DbContextOptionsBuilder<GymFlowProDbContext>().UseSqlServer(LocalDbConnectionString).Options
            : new DbContextOptionsBuilder<GymFlowProDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var shiftService = new ShiftService(ctx, auditService, NullLogger<ShiftService>.Instance);
        var invoiceService = invoiceServiceFactory?.Invoke(ctx, tenantId) ?? new NoOpInvoiceService();

        var svc = new RefundService(
            ctx, shiftService, invoiceService, new NoOpWhatsAppService(),
            new NoOpPaymobService(), new NoOpFawryService(), auditService,
            new NoOpReferralRewardService(),
            new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance),
            NullLogger<RefundService>.Instance);

        return (ctx, svc, tenantId);
    }

    private static Tenant SeedTenant(GymFlowProDbContext ctx, Guid tenantId)
    {
        var tenant = new Tenant
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
        };
        ctx.Tenants.Add(tenant);
        return tenant;
    }

    private static (AppUser staff, Guid identityUserId) SeedStaff(GymFlowProDbContext ctx, Guid tenantId, string role = "Receptionist")
    {
        var identityUserId = Guid.NewGuid();
        var staff = new AppUser
        {
            TenantId = tenantId,
            UserId = identityUserId.ToString(),
            FirstName = "Front",
            LastName = "Desk",
            Email = $"staff-{identityUserId}@test.local",
            Role = role
        };
        ctx.AppUsers.Add(staff);
        return (staff, identityUserId);
    }

    private static GymMember SeedMember(GymFlowProDbContext ctx, Guid tenantId)
    {
        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = $"M-{Guid.NewGuid():N}".Substring(0, 8),
            FullName = "Test Member",
            FullNameAr = "عضو اختبار",
            PhoneNumber = $"+2010{Random.Shared.Next(10000000, 99999999)}",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25))
        };
        ctx.GymMembers.Add(member);
        return member;
    }

    private static MembershipPlan SeedPlan(GymFlowProDbContext ctx, Guid tenantId, decimal price)
    {
        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Monthly Unlimited",
            NameAr = "شهري",
            PlanType = "monthly_unlimited",
            DurationDays = 30,
            Price = price
        };
        ctx.MembershipPlans.Add(plan);
        return plan;
    }

    /// <summary>Seeds a completed Sale + membership SaleLine + Membership, mirroring what
    /// SaleService.CreateSaleAsync produces for a real membership purchase.</summary>
    private static (Sale sale, Membership membership) SeedSaleWithMembership(
        GymFlowProDbContext ctx, Guid tenantId, Guid memberId, Guid planId, Guid soldByUserId, decimal total)
    {
        var sale = new Sale
        {
            TenantId = tenantId,
            MemberId = memberId,
            SoldByUserId = soldByUserId,
            Subtotal = total,
            Total = total,
            Status = "completed"
        };
        ctx.Sales.Add(sale);

        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = memberId,
            PlanId = planId,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = "active",
            AmountPaid = total
        };
        ctx.Memberships.Add(membership);

        ctx.SaleLines.Add(new SaleLine
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            LineType = "membership",
            ReferenceId = membership.Id,
            Description = "Monthly Unlimited",
            Qty = 1,
            UnitPrice = total,
            LineTotal = total
        });

        return (sale, membership);
    }

    [Fact]
    public async Task RequestAsync_AmountExceedsRefundableRemainder_Rejected()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var (sale, _) = SeedSaleWithMembership(ctx, tenantId, member.Id, plan.Id, staff.Id, 500m);
        await ctx.SaveChangesAsync();

        var result = await svc.RequestAsync(sale.Id, 600m, "cash", "Too much", identityUserId, tenantId);

        Assert.False(result.IsSuccess);
        Assert.StartsWith(RefundFailureReasons.RefundExceedsRemainder + "|", result.Error);
        Assert.False(await ctx.Refunds.AnyAsync());
    }

    /// <summary>
    /// A refund only reverses money already paid — it must never touch Sale.AmountDue (the
    /// still-outstanding balance), regardless of the refund amount. Guards against a naive
    /// implementation that subtracts the refund from AmountDue and could drive it negative.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_RefundOnPartiallyPaidSale_DoesNotAlterAmountDue()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (requester, requesterIdentityId) = SeedStaff(ctx, tenantId);
        var (approver, approverIdentityId) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 500m);

        // Total 500, AmountDue 200 => 300 already paid. Refund 100 (< 300 paid).
        var (sale, _) = SeedSaleWithMembership(ctx, tenantId, member.Id, plan.Id, requester.Id, 500m);
        sale.AmountDue = 200m;
        sale.Status = "partially_paid";
        await ctx.SaveChangesAsync();

        var requestResult = await svc.RequestAsync(sale.Id, 100m, "credit", "Partial refund of a paid portion", requesterIdentityId, tenantId);
        Assert.True(requestResult.IsSuccess, requestResult.Error);

        var approveResult = await svc.ApproveAsync(requestResult.Data!.Id, approverIdentityId, tenantId);
        Assert.True(approveResult.IsSuccess, approveResult.Error);

        var reloadedSale = await ctx.Sales.SingleAsync(s => s.Id == sale.Id);
        Assert.Equal(200m, reloadedSale.AmountDue);
        Assert.True(reloadedSale.AmountDue >= 0m);
    }

    [Fact]
    public async Task ApproveAsync_NonOwnerApprovingOwnRequest_FailsSelfApprovalForbidden()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId, role: "Receptionist");
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var (sale, _) = SeedSaleWithMembership(ctx, tenantId, member.Id, plan.Id, staff.Id, 500m);
        await ctx.SaveChangesAsync();

        var requestResult = await svc.RequestAsync(sale.Id, 100m, "credit", "Test", identityUserId, tenantId);
        Assert.True(requestResult.IsSuccess, requestResult.Error);

        var approveResult = await svc.ApproveAsync(requestResult.Data!.Id, identityUserId, tenantId);

        Assert.False(approveResult.IsSuccess);
        Assert.StartsWith(RefundFailureReasons.SelfApprovalForbidden + "|", approveResult.Error);
    }

    [Fact]
    public async Task ApproveAsync_OwnerApprovingOwnRequest_Succeeds()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (owner, ownerIdentityId) = SeedStaff(ctx, tenantId, role: "Owner");
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var (sale, _) = SeedSaleWithMembership(ctx, tenantId, member.Id, plan.Id, owner.Id, 500m);
        await ctx.SaveChangesAsync();

        var requestResult = await svc.RequestAsync(sale.Id, 100m, "credit", "Test", ownerIdentityId, tenantId);
        Assert.True(requestResult.IsSuccess, requestResult.Error);

        var approveResult = await svc.ApproveAsync(requestResult.Data!.Id, ownerIdentityId, tenantId);

        Assert.True(approveResult.IsSuccess, approveResult.Error);
        Assert.Equal("executed", approveResult.Data!.Status);
    }

    [Fact]
    public async Task ApproveAsync_CashRefundWithoutOpenShift_FailsOpenShiftRequired()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (requester, requesterIdentityId) = SeedStaff(ctx, tenantId);
        var (approver, approverIdentityId) = SeedStaff(ctx, tenantId); // no open shift seeded for approver
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var (sale, _) = SeedSaleWithMembership(ctx, tenantId, member.Id, plan.Id, requester.Id, 500m);
        await ctx.SaveChangesAsync();

        var requestResult = await svc.RequestAsync(sale.Id, 100m, "cash", "Test", requesterIdentityId, tenantId);
        Assert.True(requestResult.IsSuccess, requestResult.Error);

        var approveResult = await svc.ApproveAsync(requestResult.Data!.Id, approverIdentityId, tenantId);

        Assert.False(approveResult.IsSuccess);
        Assert.StartsWith(RefundFailureReasons.OpenShiftRequired + "|", approveResult.Error);
    }

    [Fact]
    public async Task ApproveAsync_FullRefund_CancelsMembershipAndSetsSaleRefunded()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (requester, requesterIdentityId) = SeedStaff(ctx, tenantId);
        var (approver, approverIdentityId) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var (sale, membership) = SeedSaleWithMembership(ctx, tenantId, member.Id, plan.Id, requester.Id, 500m);
        await ctx.SaveChangesAsync();

        var requestResult = await svc.RequestAsync(sale.Id, 500m, "credit", "Full refund", requesterIdentityId, tenantId);
        Assert.True(requestResult.IsSuccess, requestResult.Error);

        var approveResult = await svc.ApproveAsync(requestResult.Data!.Id, approverIdentityId, tenantId);
        Assert.True(approveResult.IsSuccess, approveResult.Error);

        var reloadedSale = await ctx.Sales.SingleAsync(s => s.Id == sale.Id);
        Assert.Equal("refunded", reloadedSale.Status);

        var reloadedMembership = await ctx.Memberships.SingleAsync(m => m.Id == membership.Id);
        Assert.Equal("cancelled", reloadedMembership.Status);

        var creditBalance = await ctx.MemberCredits.Where(c => c.MemberId == member.Id).SumAsync(c => c.Amount);
        Assert.Equal(500m, creditBalance);
    }

    [Fact]
    public async Task ApproveAsync_PartialRefund_LeavesMembershipUnchangedAndSetsSalePartiallyRefunded()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (requester, requesterIdentityId) = SeedStaff(ctx, tenantId);
        var (approver, approverIdentityId) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var (sale, membership) = SeedSaleWithMembership(ctx, tenantId, member.Id, plan.Id, requester.Id, 500m);
        await ctx.SaveChangesAsync();

        var requestResult = await svc.RequestAsync(sale.Id, 100m, "credit", "Partial refund", requesterIdentityId, tenantId);
        Assert.True(requestResult.IsSuccess, requestResult.Error);

        var approveResult = await svc.ApproveAsync(requestResult.Data!.Id, approverIdentityId, tenantId);
        Assert.True(approveResult.IsSuccess, approveResult.Error);

        var reloadedSale = await ctx.Sales.SingleAsync(s => s.Id == sale.Id);
        Assert.Equal("partially_refunded", reloadedSale.Status);

        var reloadedMembership = await ctx.Memberships.SingleAsync(m => m.Id == membership.Id);
        Assert.Equal("active", reloadedMembership.Status);
    }

    /// <summary>
    /// CreateCreditNoteAsync's gap-free invoice numbering uses a raw UPDATE...OUTPUT with UPDLOCK,
    /// which EF Core's InMemory provider cannot execute at all — needs a real relational engine, same
    /// as InvoiceServiceTests'/PromoServiceTests' race tests. Seeds/cleans up its own isolated rows.
    /// Uses method="cash" (not "credit") since a 'credit' refund doesn't reverse revenue — no legal
    /// credit note is issued for it (see RefundService.ApproveAsync).
    /// </summary>
    [Fact]
    public async Task ApproveAsync_CashRefund_CreditNoteCreated_WithPositiveAmountMatchingRefund()
    {
        var (ctx, svc, tenantId) = CreateSut(useLocalDb: true, (ctx2, tenantId2) =>
        {
            var localTenantContext = new TenantContext();
            localTenantContext.SetTenant(tenantId2, "Test Tenant", "Africa/Cairo");
            var auditSvc = new AuditService(ctx2, new HttpContextAccessor(), localTenantContext, NullLogger<AuditService>.Instance);
            return new InvoiceService(ctx2, auditSvc, NullLogger<InvoiceService>.Instance);
        });

        SeedTenant(ctx, tenantId);
        var (requester, requesterIdentityId) = SeedStaff(ctx, tenantId);
        var (approver, approverIdentityId) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var (sale, _) = SeedSaleWithMembership(ctx, tenantId, member.Id, plan.Id, requester.Id, 500m);
        ctx.Shifts.Add(new Shift { TenantId = tenantId, UserId = approver.Id, OpenedAt = DateTime.UtcNow, OpeningFloat = 0m, Status = "open" });
        await ctx.SaveChangesAsync();

        try
        {
            var requestResult = await svc.RequestAsync(sale.Id, 150m, "cash", "Partial refund", requesterIdentityId, tenantId);
            Assert.True(requestResult.IsSuccess, requestResult.Error);

            var approveResult = await svc.ApproveAsync(requestResult.Data!.Id, approverIdentityId, tenantId);
            Assert.True(approveResult.IsSuccess, approveResult.Error);
            Assert.NotNull(approveResult.Data!.CreditNoteInvoiceId);

            var creditNote = await ctx.Invoices.SingleAsync(i => i.Id == approveResult.Data.CreditNoteInvoiceId!.Value);
            Assert.Equal("credit_note", creditNote.Type);
            Assert.Equal(150m, creditNote.Total);
            Assert.Equal(sale.Id, creditNote.SaleId);
            Assert.Equal(approveResult.Data.Id, creditNote.RefundId);
        }
        finally
        {
            await ctx.CashMovements.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Shifts.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.AuditEvents.Where(a => a.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Invoices.Where(i => i.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Refunds.Where(r => r.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.MemberCredits.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Memberships.Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.SaleLines.Where(l => l.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Sales.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.GymMembers.Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.MembershipPlans.Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.AppUsers.Where(u => u.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// Regression guard for the fix above: a 'credit' refund is a store-credit issuance, not a
    /// revenue reversal, so it must NOT trigger a legal credit note. Runs on InMemory (no LocalDB
    /// needed) since this only checks whether IInvoiceService.CreateCreditNoteAsync was called.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_CreditRefund_DoesNotCreateCreditNote()
    {
        var recordingInvoiceService = new RecordingInvoiceService();
        var (ctx, svc, tenantId) = CreateSut(invoiceServiceFactory: (ctx2, tenantId2) => recordingInvoiceService);

        SeedTenant(ctx, tenantId);
        var (requester, requesterIdentityId) = SeedStaff(ctx, tenantId);
        var (approver, approverIdentityId) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var (sale, _) = SeedSaleWithMembership(ctx, tenantId, member.Id, plan.Id, requester.Id, 500m);
        await ctx.SaveChangesAsync();

        var requestResult = await svc.RequestAsync(sale.Id, 150m, "credit", "Credit refund", requesterIdentityId, tenantId);
        Assert.True(requestResult.IsSuccess, requestResult.Error);

        var approveResult = await svc.ApproveAsync(requestResult.Data!.Id, approverIdentityId, tenantId);
        Assert.True(approveResult.IsSuccess, approveResult.Error);

        Assert.Null(approveResult.Data!.CreditNoteInvoiceId);
        Assert.Empty(recordingInvoiceService.CreateCreditNoteCalls);

        var creditBalance = await ctx.MemberCredits.Where(c => c.MemberId == member.Id).SumAsync(c => c.Amount);
        Assert.Equal(150m, creditBalance);
    }

    /// <summary>
    /// The account_credit payment leg's balance check uses a raw UPDLOCK'd SUM query — proving it's
    /// actually race-safe requires real row locking, same as PromoServiceTests'/SaleServiceTests'
    /// race tests. Drives SaleService.CreateSaleAsync directly (that's where the lock is exercised),
    /// not RefundService. Seeds/cleans up its own isolated rows.
    /// </summary>
    [Fact]
    public async Task AccountCreditSpend_TwoConcurrentSalesAgainst100Balance_OnlyOneOf80EachSucceeds()
    {
        const string connectionString = LocalDbConnectionString;

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        Guid planId;
        Guid staffAppUserId;
        var staffIdentityId = Guid.NewGuid();

        await using (var seed = new GymFlowProDbContext(options, null))
        {
            SeedTenant(seed, tenantId);
            var member = new GymMember
            {
                Id = memberId,
                TenantId = tenantId,
                MemberNumber = $"M-{Guid.NewGuid():N}".Substring(0, 8),
                FullName = "Race Member",
                FullNameAr = "عضو",
                PhoneNumber = $"+2010{Random.Shared.Next(10000000, 99999999)}",
                DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25))
            };
            seed.GymMembers.Add(member);

            var plan = SeedPlan(seed, tenantId, 80m);
            planId = plan.Id;

            var staff = new AppUser
            {
                TenantId = tenantId,
                UserId = staffIdentityId.ToString(),
                FirstName = "Front",
                LastName = "Desk",
                Email = $"staff-{staffIdentityId}@test.local",
                Role = "Receptionist"
            };
            seed.AppUsers.Add(staff);

            await seed.SaveChangesAsync();
            staffAppUserId = staff.Id;

            seed.MemberCredits.Add(new MemberCredit
            {
                TenantId = tenantId,
                MemberId = memberId,
                Amount = 100m,
                EntryType = "adjustment",
                CreatedByUserId = staffAppUserId
            });
            await seed.SaveChangesAsync();
        }

        try
        {
            var results = new Result<SaleResponse>[2];

            await Parallel.ForEachAsync(Enumerable.Range(0, 2), async (i, _) =>
            {
                var tenantContext = new TenantContext();
                tenantContext.SetTenant(tenantId, "Race Tenant", "Africa/Cairo");

                await using var ctx = new GymFlowProDbContext(options, tenantContext);
                var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
                var memberService = new MemberService(
                    ctx, new MemberRepository(ctx),
                    new AesEncryptionService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()),
                    new UnlimitedTierEnforcement(),
            new NoOpReferralAttribution(),
            new NoOpMemberAppActivation(),
            NullLogger<MemberService>.Instance);
                var promoService = new PromoService(ctx, new Repository<PromoCode>(ctx), tenantContext, NullLogger<PromoService>.Instance);
                var shiftService = new ShiftService(ctx, auditService, NullLogger<ShiftService>.Instance);

                var svc = new SaleService(
                    ctx, memberService, promoService, auditService, shiftService,
                    new NoOpInvoiceService(), new NoOpWhatsAppService(),
                    new NoOpReferralAttribution(),
                    new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance),
                    new AlwaysEnabledFeatureAccess(),
                    NullLogger<SaleService>.Instance);

                var request = new CreateSaleRequest
                {
                    MemberId = memberId,
                    PlanId = planId,
                    Payments = new List<SalePaymentRequest> { new() { Method = "account_credit", Amount = 80m } }
                };

                results[i] = await svc.CreateSaleAsync(request, staffIdentityId, tenantId, new HashSet<string>());
            });

            Assert.Single(results, r => r.IsSuccess);
            Assert.Single(results, r => !r.IsSuccess && r.Error!.StartsWith(SaleFailureReasons.InsufficientCredit + "|"));
        }
        finally
        {
            // Cleanup must use a real (non-null) ITenantContext: these LINQ .Where().ExecuteDeleteAsync()
            // calls hit tenant-scoped entities whose compiled query filter dereferences
            // _tenantContext.TenantId — a null-tenantContext instance NullReferenceExceptions once the
            // model's filter has been baked in by any other test in this process (see cerebrum.md).
            var cleanupTenantContext = new TenantContext();
            cleanupTenantContext.SetTenant(tenantId, "Race Tenant", "Africa/Cairo");
            await using var cleanup = new GymFlowProDbContext(options, cleanupTenantContext);
            await cleanup.PaymentTransactions.Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
            await cleanup.MemberCredits.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
            await cleanup.Memberships.Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
            await cleanup.SaleLines.Where(l => l.TenantId == tenantId).ExecuteDeleteAsync();
            await cleanup.Sales.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
            await cleanup.GymMembers.Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
            await cleanup.MembershipPlans.Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
            await cleanup.AppUsers.Where(u => u.TenantId == tenantId).ExecuteDeleteAsync();
            await cleanup.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ApproveAsync_FullRetailRefund_RestoresStock()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (requester, requesterIdentityId) = SeedStaff(ctx, tenantId);
        var (approver, approverIdentityId) = SeedStaff(ctx, tenantId);
        var (sale, line, warehouseId, productId) = await SeedRetailSaleWithStockAsync(ctx, tenantId, requester.Id, qty: 2, onHandAfterSale: 8);
        await ctx.SaveChangesAsync();

        var requestResult = await svc.RequestAsync(sale.Id, sale.Total, "credit", "Customer return", requesterIdentityId, tenantId);
        Assert.True(requestResult.IsSuccess, requestResult.Error);

        var approveResult = await svc.ApproveAsync(requestResult.Data!.Id, approverIdentityId, tenantId);
        Assert.True(approveResult.IsSuccess, approveResult.Error);
        Assert.True(approveResult.Data!.StockRestored);
        Assert.Equal("executed", approveResult.Data.Status);

        var onHand = await ctx.StockBalances.SingleAsync(b =>
            b.ProductId == productId && b.WarehouseId == warehouseId && b.BatchId == null);
        Assert.Equal(10, onHand.QtyOnHand);

        var refundMove = await ctx.StockMovements.SingleAsync(m =>
            m.Reason == StockMovementReasons.SaleRefund
            && m.ReferenceType == StockReferenceTypes.RefundSaleLine
            && m.ReferenceId == line.Id);
        Assert.Equal(2, refundMove.QtyDelta);
        Assert.Equal(warehouseId, refundMove.WarehouseId);

        var saleRow = await ctx.Sales.SingleAsync(s => s.Id == sale.Id);
        Assert.Equal("refunded", saleRow.Status);
    }

    [Fact]
    public async Task ApproveAsync_PartialRetailRefund_DoesNotRestoreStock()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (requester, requesterIdentityId) = SeedStaff(ctx, tenantId);
        var (approver, approverIdentityId) = SeedStaff(ctx, tenantId);
        var (sale, _, warehouseId, productId) = await SeedRetailSaleWithStockAsync(ctx, tenantId, requester.Id, qty: 2, onHandAfterSale: 8);
        await ctx.SaveChangesAsync();

        var requestResult = await svc.RequestAsync(sale.Id, sale.Total / 2, "credit", "Partial", requesterIdentityId, tenantId);
        Assert.True(requestResult.IsSuccess, requestResult.Error);

        var approveResult = await svc.ApproveAsync(requestResult.Data!.Id, approverIdentityId, tenantId);
        Assert.True(approveResult.IsSuccess, approveResult.Error);
        Assert.False(approveResult.Data!.StockRestored);

        var onHand = await ctx.StockBalances.SingleAsync(b =>
            b.ProductId == productId && b.WarehouseId == warehouseId);
        Assert.Equal(8, onHand.QtyOnHand);
        Assert.False(await ctx.StockMovements.AnyAsync(m => m.Reason == StockMovementReasons.SaleRefund));

        var saleRow = await ctx.Sales.SingleAsync(s => s.Id == sale.Id);
        Assert.Equal("partially_refunded", saleRow.Status);
    }

    [Fact]
    public async Task ApproveAsync_MixedSaleFullRefund_RestoresOnlyRetailLines()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (requester, requesterIdentityId) = SeedStaff(ctx, tenantId);
        var (approver, approverIdentityId) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 200m);
        var (sale, membership) = SeedSaleWithMembership(ctx, tenantId, member.Id, plan.Id, requester.Id, 200m);

        var product = new Product
        {
            TenantId = tenantId, Sku = "MIX-1", Name = "Snack", UnitOfMeasure = "pcs",
            SellPrice = 50m, CostPrice = 20m, Currency = "EGP", TrackStock = true,
            IsActive = true, IsSellable = true, CreatedAtUtc = DateTime.UtcNow
        };
        var warehouse = new Warehouse
        {
            TenantId = tenantId, Code = "MAIN", Name = "Main", IsDefault = true,
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Products.Add(product);
        ctx.Warehouses.Add(warehouse);
        await ctx.SaveChangesAsync();

        var ledger = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);
        Assert.True((await ledger.PostAsync(new GMS.Application.DTOs.Inventory.StockLedgerPostRequest
        {
            TenantId = tenantId, ProductId = product.Id, WarehouseId = warehouse.Id,
            QtyDelta = 5, Reason = StockMovementReasons.Opening,
            ReferenceType = "TestSeed", ReferenceId = Guid.NewGuid()
        })).IsSuccess);

        var retailLine = new SaleLine
        {
            TenantId = tenantId, SaleId = sale.Id, LineType = "retail",
            ReferenceId = product.Id, Description = product.Name, Qty = 1,
            UnitPrice = 50m, LineTotal = 50m
        };
        ctx.SaleLines.Add(retailLine);
        sale.Subtotal = 250m;
        sale.Total = 250m;
        await ctx.SaveChangesAsync();

        Assert.True((await ledger.PostAsync(new GMS.Application.DTOs.Inventory.StockLedgerPostRequest
        {
            TenantId = tenantId, ProductId = product.Id, WarehouseId = warehouse.Id,
            QtyDelta = -1, UnitCost = 20m, Reason = StockMovementReasons.Sale,
            ReferenceType = StockReferenceTypes.SaleLine, ReferenceId = retailLine.Id
        })).IsSuccess);

        var requestResult = await svc.RequestAsync(sale.Id, 250m, "credit", "Full mixed", requesterIdentityId, tenantId);
        Assert.True(requestResult.IsSuccess, requestResult.Error);
        var approveResult = await svc.ApproveAsync(requestResult.Data!.Id, approverIdentityId, tenantId);
        Assert.True(approveResult.IsSuccess, approveResult.Error);
        Assert.True(approveResult.Data!.StockRestored);

        Assert.Equal(5, (await ctx.StockBalances.SingleAsync(b => b.ProductId == product.Id)).QtyOnHand);
        Assert.Equal("cancelled", (await ctx.Memberships.SingleAsync(m => m.Id == membership.Id)).Status);
        Assert.Equal(1, await ctx.StockMovements.CountAsync(m => m.Reason == StockMovementReasons.SaleRefund));
    }

    private static async Task<(Sale sale, SaleLine line, Guid warehouseId, Guid productId)> SeedRetailSaleWithStockAsync(
        GymFlowProDbContext ctx, Guid tenantId, Guid soldByUserId, int qty, decimal onHandAfterSale)
    {
        var product = new Product
        {
            TenantId = tenantId,
            Sku = $"SKU-{Guid.NewGuid():N}"[..12],
            Name = "Protein",
            UnitOfMeasure = "pcs",
            SellPrice = 100m,
            CostPrice = 40m,
            Currency = "EGP",
            TrackStock = true,
            IsActive = true,
            IsSellable = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var warehouse = new Warehouse
        {
            TenantId = tenantId,
            Code = "MAIN",
            Name = "Main",
            IsDefault = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Products.Add(product);
        ctx.Warehouses.Add(warehouse);
        await ctx.SaveChangesAsync();

        var openingQty = onHandAfterSale + qty;
        var ledger = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);
        var open = await ledger.PostAsync(new GMS.Application.DTOs.Inventory.StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            QtyDelta = openingQty,
            UnitCost = 40m,
            Reason = StockMovementReasons.Opening,
            ReferenceType = "TestSeed",
            ReferenceId = Guid.NewGuid()
        });
        Assert.True(open.IsSuccess, open.Error);

        var total = product.SellPrice * qty;
        var sale = new Sale
        {
            TenantId = tenantId,
            SoldByUserId = soldByUserId,
            Subtotal = total,
            Total = total,
            Status = "completed"
        };
        ctx.Sales.Add(sale);

        var line = new SaleLine
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            LineType = "retail",
            ReferenceId = product.Id,
            Description = product.Name,
            Qty = qty,
            UnitPrice = product.SellPrice,
            LineTotal = total
        };
        ctx.SaleLines.Add(line);
        await ctx.SaveChangesAsync();

        var salePost = await ledger.PostAsync(new GMS.Application.DTOs.Inventory.StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            QtyDelta = -qty,
            UnitCost = product.CostPrice,
            Reason = StockMovementReasons.Sale,
            ReferenceType = StockReferenceTypes.SaleLine,
            ReferenceId = line.Id
        });
        Assert.True(salePost.IsSuccess, salePost.Error);

        return (sale, line, warehouse.Id, product.Id);
    }
}
