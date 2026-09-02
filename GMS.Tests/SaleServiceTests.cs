namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Common;
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

public class SaleServiceTests
{
    private class NoOpInvoiceService : IInvoiceService
    {
        public Task EnqueueForSale(Guid saleId) => Task.CompletedTask;
        public Task CreateForSaleAsync(Guid saleId) => Task.CompletedTask;
        public Task<Result<Guid>> CreateCreditNoteAsync(Guid refundId) =>
            Task.FromResult(Result<Guid>.Failure("not implemented in test double"));
        public Task<Result<PagedResult<GMS.Application.DTOs.Invoices.InvoiceDto>>> GetPagedAsync(
            Guid tenantId, GMS.Application.DTOs.Invoices.InvoiceQueryRequest query) =>
            Task.FromResult(Result<PagedResult<GMS.Application.DTOs.Invoices.InvoiceDto>>.Failure("not implemented in test double"));
        public Task<Result<GMS.Application.DTOs.Invoices.InvoiceDto>> GetByIdAsync(Guid id) =>
            Task.FromResult(Result<GMS.Application.DTOs.Invoices.InvoiceDto>.Failure("not implemented in test double"));
        public Task<Result<bool>> ResendAsync(Guid invoiceId) =>
            Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> VoidAsync(Guid invoiceId, string reason, Guid voidedByUserId) =>
            Task.FromResult(Result<bool>.Success(true));
        public Task<Result<GMS.Application.DTOs.Invoices.PaymentReceiptInfoDto>> GetPaymentInfoAsync(Guid paymentTransactionId) =>
            Task.FromResult(Result<GMS.Application.DTOs.Invoices.PaymentReceiptInfoDto>.Failure("not implemented in test double"));
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

    private const string LocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;";

    private static (GymFlowProDbContext ctx, SaleService svc, Guid tenantId) CreateSut(
        bool useLocalDb = false, Guid? tenantIdOverride = null)
    {
        var tenantId = tenantIdOverride ?? Guid.NewGuid();

        var options = useLocalDb
            ? new DbContextOptionsBuilder<GymFlowProDbContext>().UseSqlServer(LocalDbConnectionString).Options
            : new DbContextOptionsBuilder<GymFlowProDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);

        var memberService = new MemberService(
            ctx, new MemberRepository(ctx), new AesEncryptionService(new ConfigurationBuilder().Build()), new UnlimitedTierEnforcement(), new NoOpReferralAttribution(), new NoOpMemberAppActivation(), new ActivityEntitlementService(ctx),
            NullLogger<MemberService>.Instance);
        var promoService = new PromoService(ctx, new Repository<PromoCode>(ctx), tenantContext, NullLogger<PromoService>.Instance);
        var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var shiftService = new ShiftService(ctx, auditService, NullLogger<ShiftService>.Instance);

        var ledger = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);

        var svc = new SaleService(
            ctx, memberService, promoService, auditService,
            shiftService, new NoOpInvoiceService(), new NoOpWhatsAppService(),
            new NoOpReferralAttribution(),
            ledger, new AlwaysEnabledFeatureAccess(),
            NullLogger<SaleService>.Instance);

        return (ctx, svc, tenantId);
    }

    /// <summary>Seeds an open shift directly (bypassing ShiftService.OpenAsync) so cash-payment
    /// tests aren't blocked by the real IShiftService now that NullShiftService is gone.</summary>
    private static Shift SeedOpenShift(GymFlowProDbContext ctx, Guid tenantId, Guid appUserId, decimal openingFloat = 0m)
    {
        var shift = new Shift
        {
            TenantId = tenantId,
            UserId = appUserId,
            OpenedAt = DateTime.UtcNow,
            OpeningFloat = openingFloat,
            Status = "open"
        };
        ctx.Shifts.Add(shift);
        return shift;
    }

    private static Tenant SeedTenant(GymFlowProDbContext ctx, Guid tenantId, string? settingsJson = null)
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
            SubscriptionStartDate = DateTime.UtcNow,
            Settings = settingsJson
        };
        ctx.Tenants.Add(tenant);
        return tenant;
    }

    private static (AppUser staff, Guid identityUserId) SeedStaff(GymFlowProDbContext ctx, Guid tenantId)
    {
        var identityUserId = Guid.NewGuid();
        var staff = new AppUser
        {
            TenantId = tenantId,
            UserId = identityUserId.ToString(),
            FirstName = "Front",
            LastName = "Desk",
            Email = $"staff-{identityUserId}@test.local",
            Role = "Receptionist"
        };
        ctx.AppUsers.Add(staff);
        return (staff, identityUserId);
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

    private static GymMember SeedMember(GymFlowProDbContext ctx, Guid tenantId, bool paperWaiverOnFile = true)
    {
        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = $"M-{Guid.NewGuid():N}".Substring(0, 8),
            FullName = "Test Member",
            FullNameAr = "عضو اختبار",
            PhoneNumber = $"+2010{Random.Shared.Next(10000000, 99999999)}",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25)),
            PaperWaiverOnFile = paperWaiverOnFile
        };
        ctx.GymMembers.Add(member);
        return member;
    }

    private static CreateSaleRequest BuildRequest(Guid memberId, Guid planId, params (string method, decimal amount)[] payments) => new()
    {
        MemberId = memberId,
        PlanId = planId,
        Payments = payments.Select(p => new SalePaymentRequest { Method = p.method, Amount = p.amount }).ToList()
    };

    [Fact]
    public async Task CreateSaleAsync_FullCashPayment_CreatesSaleMembershipAndPaymentAtomically()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var member = SeedMember(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var request = BuildRequest(member.Id, plan.Id, ("cash", 500m));
        var result = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(500m, result.Data!.Totals.Total);
        Assert.Equal(0m, result.Data.Totals.AmountDue);

        var sale = await ctx.Sales.SingleAsync(s => s.Id == result.Data.SaleId);
        Assert.Equal("completed", sale.Status);

        var membership = await ctx.Memberships.SingleAsync(m => m.Id == result.Data.MembershipId);
        Assert.Equal("active", membership.Status);
        Assert.Equal(plan.Id, membership.PlanId);

        var payment = await ctx.PaymentTransactions.SingleAsync(p => p.SaleId == sale.Id);
        Assert.Equal(500m, payment.Amount);
        Assert.Equal("cash", payment.Method);
        Assert.Equal(membership.Id, payment.MembershipId);
        Assert.Equal(staff.Id, payment.ReceivedByUserId);

        // Cash sale should also have recorded a 'sale' cash movement against the open shift.
        var shift = await ctx.Shifts.Include(s => s.Movements).SingleAsync(s => s.UserId == staff.Id);
        Assert.Single(shift.Movements);
        Assert.Equal("sale", shift.Movements.First().Type);
        Assert.Equal(500m, shift.Movements.First().Amount);
    }

    /// <summary>
    /// The account_credit payment leg validates against the member's ledger balance via a raw
    /// UPDLOCK'd SUM query, which EF Core's InMemory provider cannot execute at all — needs a real
    /// relational engine, same as the promo-race test below. Seeds/cleans up its own isolated rows.
    /// </summary>
    [Fact]
    public async Task CreateSaleAsync_SplitPayment_CreatesTwoPaymentTransactionsWithCorrectAmounts()
    {
        var (ctx, svc, tenantId) = CreateSut(useLocalDb: true);
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var member = SeedMember(ctx, tenantId);
        ctx.MemberCredits.Add(new MemberCredit
        {
            TenantId = tenantId,
            MemberId = member.Id,
            Amount = 200m,
            EntryType = "adjustment",
            CreatedByUserId = staff.Id
        });
        await ctx.SaveChangesAsync();

        try
        {
            var request = BuildRequest(member.Id, plan.Id, ("cash", 300m), ("account_credit", 200m));
            var result = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());

            Assert.True(result.IsSuccess, result.Error);

            var payments = await ctx.PaymentTransactions
                .Where(p => p.SaleId == result.Data!.SaleId)
                .OrderBy(p => p.Method)
                .ToListAsync();

            Assert.Equal(2, payments.Count);
            Assert.Equal(200m, payments.Single(p => p.Method == "account_credit").Amount);
            Assert.Equal(300m, payments.Single(p => p.Method == "cash").Amount);

            var creditEntries = await ctx.MemberCredits.Where(c => c.MemberId == member.Id).SumAsync(c => c.Amount);
            Assert.Equal(0m, creditEntries); // +200 adjustment, -200 payment_use
        }
        finally
        {
            await ctx.PaymentTransactions.Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.MemberCredits.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Memberships.Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.SaleLines.Where(l => l.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Sales.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.GymMembers.Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.MembershipPlans.Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Shifts.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.AppUsers.Where(u => u.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task CreateSaleAsync_PartialPayment_LeavesAmountDueAndSetsPartiallyPaidStatus()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var member = SeedMember(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var request = BuildRequest(member.Id, plan.Id, ("cash", 200m));
        request.PartialPayment = new PartialPaymentRequest { DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)) };

        var result = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(300m, result.Data!.Totals.AmountDue);

        var sale = await ctx.Sales.SingleAsync(s => s.Id == result.Data.SaleId);
        Assert.Equal("partially_paid", sale.Status);
        Assert.Equal(300m, sale.AmountDue);

        // Membership still grants immediate access even though a balance remains.
        var membership = await ctx.Memberships.SingleAsync(m => m.Id == result.Data.MembershipId);
        Assert.Equal("active", membership.Status);
    }

    [Fact]
    public async Task CreateSaleAsync_PaymentExceedsTotal_FailsWithOverpayReason()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (_, identityUserId) = SeedStaff(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var member = SeedMember(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var request = BuildRequest(member.Id, plan.Id, ("cash", 600m));
        var result = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());

        Assert.False(result.IsSuccess);
        Assert.StartsWith(SaleFailureReasons.Overpay + "|", result.Error);
    }

    [Fact]
    public async Task CreateSaleAsync_ManualDiscountWithoutPermission_FailsWithForbiddenReason()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (_, identityUserId) = SeedStaff(ctx, tenantId);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var member = SeedMember(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var request = BuildRequest(member.Id, plan.Id, ("cash", 450m));
        request.ManualDiscount = new ManualDiscountRequest { Amount = 50m, Reason = "loyalty" };

        var result = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());

        Assert.False(result.IsSuccess);
        Assert.StartsWith(SaleFailureReasons.ForbiddenDiscountOverride + "|", result.Error);

        Assert.False(await ctx.Sales.AnyAsync());
    }

    [Fact]
    public async Task CreateSaleAsync_ManualDiscountWithPermission_Succeeds()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var member = SeedMember(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var request = BuildRequest(member.Id, plan.Id, ("cash", 450m));
        request.ManualDiscount = new ManualDiscountRequest { Amount = 50m, Reason = "loyalty" };

        var result = await svc.CreateSaleAsync(
            request, identityUserId, tenantId, new HashSet<string> { Permissions.SalesDiscountOverride });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(50m, result.Data!.Totals.Discount);
        Assert.Equal(450m, result.Data.Totals.Total);
    }

    [Fact]
    public async Task CreateSaleAsync_IdempotentReplay_ReturnsSameSaleIdWithIsReplayTrue()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var member = SeedMember(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var request = BuildRequest(member.Id, plan.Id, ("cash", 500m));
        request.IdempotencyKey = "test-key-123";

        var first = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());
        Assert.True(first.IsSuccess, first.Error);
        Assert.False(first.Data!.IsReplay);

        var second = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());
        Assert.True(second.IsSuccess, second.Error);
        Assert.True(second.Data!.IsReplay);
        Assert.Equal(first.Data.SaleId, second.Data.SaleId);

        Assert.Equal(1, await ctx.Sales.CountAsync());
    }

    [Fact]
    public async Task CreateSaleAsync_PaperWaiverRequiredButNotOnFile_AddsWarningWithoutBlocking()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId, settingsJson: "{\"require_paper_waiver\":true}");
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var member = SeedMember(ctx, tenantId, paperWaiverOnFile: false);
        await ctx.SaveChangesAsync();

        var request = BuildRequest(member.Id, plan.Id, ("cash", 500m));
        var result = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Data!.Warnings);
    }

    /// <summary>
    /// Promo consumption uses IPromoService.TryConsumeAsync's raw conditional UPDATE, which EF Core's
    /// InMemory provider cannot execute at all — this needs a real relational engine, same as
    /// PromoServiceTests' race test. Seeds/cleans up its own isolated tenant/plan/member/promo rows.
    /// </summary>
    [Fact]
    public async Task CreateSaleAsync_PromoRace_WinnerConsumesOnce_LoserLeavesNoOrphanSale()
    {
        var (ctx, svc, tenantId) = CreateSut(useLocalDb: true);

        var tenant = SeedTenant(ctx, tenantId);
        var (staff1, identityUserId1) = SeedStaff(ctx, tenantId);
        var (staff2, identityUserId2) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff1.Id);
        SeedOpenShift(ctx, tenantId, staff2.Id);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var memberA = SeedMember(ctx, tenantId);
        var memberB = SeedMember(ctx, tenantId);

        var promo = new PromoCode
        {
            TenantId = tenantId,
            Code = "RACE10",
            Type = "percent",
            Value = 10,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            ValidTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            MaxUses = 1,
            IsActive = true
        };
        ctx.PromoCodes.Add(promo);

        await ctx.SaveChangesAsync();

        try
        {
            var requestA = BuildRequest(memberA.Id, plan.Id, ("cash", 450m));
            requestA.PromoCode = "RACE10";

            var requestB = BuildRequest(memberB.Id, plan.Id, ("cash", 450m));
            requestB.PromoCode = "RACE10";

            var resultA = await svc.CreateSaleAsync(requestA, identityUserId1, tenantId, new HashSet<string>());
            var resultB = await svc.CreateSaleAsync(requestB, identityUserId2, tenantId, new HashSet<string>());

            var results = new[] { resultA, resultB };
            Assert.Single(results, r => r.IsSuccess);
            Assert.Single(results, r => !r.IsSuccess && r.Error!.StartsWith(SaleFailureReasons.PromoRaceLost + "|"));

            // Only the winner's Sale (and its Membership/PaymentTransaction) exist — the loser left no orphan rows.
            Assert.Equal(1, await ctx.Sales.CountAsync(s => s.TenantId == tenantId));
            Assert.Equal(1, await ctx.Memberships.CountAsync(m => m.TenantId == tenantId));
            Assert.Equal(1, await ctx.PaymentTransactions.CountAsync(p => p.TenantId == tenantId));

            var reloadedPromo = await ctx.PromoCodes.AsNoTracking().SingleAsync(p => p.Id == promo.Id);
            Assert.Equal(1, reloadedPromo.UsesCount);
        }
        finally
        {
            await ctx.PaymentTransactions.Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Memberships.Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.SaleLines.Where(l => l.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Sales.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.PromoCodes.Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.GymMembers.Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.MembershipPlans.Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.CashMovements.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Shifts.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.AppUsers.Where(u => u.TenantId == tenantId).ExecuteDeleteAsync();
            await ctx.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task CreateSaleAsync_CashPaymentWithoutOpenShift_FailsWithOpenShiftRequired()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (_, identityUserId) = SeedStaff(ctx, tenantId); // no SeedOpenShift — none open
        var plan = SeedPlan(ctx, tenantId, 500m);
        var member = SeedMember(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var request = BuildRequest(member.Id, plan.Id, ("cash", 500m));
        var result = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());

        Assert.False(result.IsSuccess);
        Assert.StartsWith(SaleFailureReasons.OpenShiftRequired + "|", result.Error);
        Assert.False(await ctx.Sales.AnyAsync());
    }

    [Fact]
    public async Task CreateSaleAsync_TrialMemberBuysRealPlan_ConvertsTrialAndPreservesAttendanceHistory()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var member = SeedMember(ctx, tenantId);
        member.IsTrial = true;
        member.TrialOutcome = "active_trial";

        // Attendance history recorded during the trial must survive conversion untouched.
        ctx.GymAttendances.Add(new GymAttendance
        {
            TenantId = tenantId,
            MemberId = member.Id,
            MembershipId = Guid.NewGuid(),
            CheckInAtUtc = DateTime.UtcNow.AddDays(-1),
            EntryMethod = "manual"
        });
        await ctx.SaveChangesAsync();

        var attendanceCountBefore = await ctx.GymAttendances.CountAsync(a => a.MemberId == member.Id);

        var request = BuildRequest(member.Id, plan.Id, ("cash", 500m));
        var result = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());

        Assert.True(result.IsSuccess, result.Error);

        var reloadedMember = await ctx.GymMembers.SingleAsync(m => m.Id == member.Id);
        Assert.False(reloadedMember.IsTrial);
        Assert.Equal("converted", reloadedMember.TrialOutcome);
        Assert.NotNull(reloadedMember.TrialConvertedAt);
        Assert.Equal(result.Data!.SaleId, reloadedMember.ConvertingSaleId);

        var attendanceCountAfter = await ctx.GymAttendances.CountAsync(a => a.MemberId == member.Id);
        Assert.Equal(attendanceCountBefore, attendanceCountAfter);
    }

    /// <summary>
    /// Regression guard: RecordPaymentAsync previously never recorded the cash it received against
    /// the shift, so a cash debt payment left ExpectedCash silently under-counting real drawer cash
    /// (found via the reconciliation invariant tests — see cerebrum.md/buglog bug-054 area).
    /// </summary>
    [Fact]
    public async Task RecordPaymentAsync_CashDebtPayment_RecordsCashMovementAgainstOpenShift()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var plan = SeedPlan(ctx, tenantId, 500m);
        var member = SeedMember(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var request = BuildRequest(member.Id, plan.Id, ("cash", 200m));
        request.PartialPayment = new PartialPaymentRequest { DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)) };
        var saleResult = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());
        Assert.True(saleResult.IsSuccess, saleResult.Error);

        var paymentResult = await svc.RecordPaymentAsync(
            saleResult.Data!.SaleId, tenantId, identityUserId, new RecordPaymentRequest { Method = "cash", Amount = 300m });
        Assert.True(paymentResult.IsSuccess, paymentResult.Error);
        Assert.Equal(0m, paymentResult.Data!.Totals.AmountDue);

        var shift = await ctx.Shifts.Include(s => s.Movements).SingleAsync(s => s.UserId == staff.Id);
        Assert.Equal(2, shift.Movements.Count); // one from the initial sale, one from the debt payment
        Assert.Equal(500m, shift.Movements.Sum(m => m.Amount)); // 200 (sale) + 300 (debt payment)
    }

    [Fact]
    public async Task RecordPaymentAsync_CompletedOrRefundedSale_Rejected()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var member = SeedMember(ctx, tenantId);
        var completed = new Sale
        {
            TenantId = tenantId,
            MemberId = member.Id,
            SoldByUserId = staff.Id,
            Subtotal = 800m,
            Total = 800m,
            AmountDue = 0m,
            Status = "completed"
        };
        var refunded = new Sale
        {
            TenantId = tenantId,
            MemberId = member.Id,
            SoldByUserId = staff.Id,
            Subtotal = 300m,
            Total = 300m,
            AmountDue = 300m,
            Status = "refunded"
        };
        ctx.Sales.AddRange(completed, refunded);
        await ctx.SaveChangesAsync();

        var done = await svc.RecordPaymentAsync(
            completed.Id, tenantId, identityUserId, new RecordPaymentRequest { Method = "cash", Amount = 100m });
        Assert.False(done.IsSuccess);
        Assert.StartsWith(SaleFailureReasons.SaleNotCollectable + "|", done.Error);

        var refundedPay = await svc.RecordPaymentAsync(
            refunded.Id, tenantId, identityUserId, new RecordPaymentRequest { Method = "cash", Amount = 100m });
        Assert.False(refundedPay.IsSuccess);
        Assert.StartsWith(SaleFailureReasons.SaleNotCollectable + "|", refundedPay.Error);
    }

    [Fact]
    public async Task RetailOnly_WalkInCash_DeductsStock_NullMembershipId()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var (product, warehouse) = await SeedStockedProductAsync(ctx, tenantId, qty: 10);
        await ctx.SaveChangesAsync();

        var result = await svc.CreateSaleAsync(new CreateSaleRequest
        {
            Lines = new List<CreateSaleLineRequest>
            {
                new() { LineType = "retail", ProductId = product.Id, Qty = 2 }
            },
            Payments = new List<SalePaymentRequest> { new() { Method = "cash", Amount = product.SellPrice * 2 } }
        }, identityUserId, tenantId, new HashSet<string>());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Data!.MembershipId);

        var line = await ctx.SaleLines.SingleAsync(l => l.SaleId == result.Data.SaleId);
        Assert.Equal("retail", line.LineType);
        Assert.Equal(product.Id, line.ReferenceId);
        Assert.Equal(2, line.Qty);

        var movements = await ctx.StockMovements.Where(m => m.ReferenceId == line.Id).ToListAsync();
        Assert.Single(movements);
        Assert.Equal(-2, movements[0].QtyDelta);
        Assert.Equal(StockMovementReasons.Sale, movements[0].Reason);

        var payment = await ctx.PaymentTransactions.SingleAsync(p => p.SaleId == result.Data.SaleId);
        Assert.Null(payment.MembershipId);
        Assert.Null(payment.MemberId);

        var onHand = await ctx.StockBalances.SingleAsync(b =>
            b.ProductId == product.Id && b.WarehouseId == warehouse.Id && b.BatchId == null);
        Assert.Equal(8, onHand.QtyOnHand);
    }

    [Fact]
    public async Task Retail_InsufficientStock_Rejected()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var (product, _) = await SeedStockedProductAsync(ctx, tenantId, qty: 1);
        await ctx.SaveChangesAsync();

        var result = await svc.CreateSaleAsync(new CreateSaleRequest
        {
            Lines = new List<CreateSaleLineRequest>
            {
                new() { LineType = "retail", ProductId = product.Id, Qty = 2 }
            },
            Payments = new List<SalePaymentRequest> { new() { Method = "cash", Amount = product.SellPrice * 2 } }
        }, identityUserId, tenantId, new HashSet<string>());

        Assert.False(result.IsSuccess);
        Assert.StartsWith(SaleFailureReasons.InsufficientStock + "|", result.Error);
        Assert.Contains(product.Sku, result.Error);
    }

    [Fact]
    public async Task Retail_IdempotentRetry_OneStockMovement()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var (product, _) = await SeedStockedProductAsync(ctx, tenantId, qty: 5);
        await ctx.SaveChangesAsync();

        var request = new CreateSaleRequest
        {
            IdempotencyKey = $"retail-{Guid.NewGuid():N}",
            Lines = new List<CreateSaleLineRequest>
            {
                new() { LineType = "retail", ProductId = product.Id, Qty = 1 }
            },
            Payments = new List<SalePaymentRequest> { new() { Method = "cash", Amount = product.SellPrice } }
        };

        var first = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());
        Assert.True(first.IsSuccess, first.Error);
        var second = await svc.CreateSaleAsync(request, identityUserId, tenantId, new HashSet<string>());
        Assert.True(second.IsSuccess, second.Error);
        Assert.True(second.Data!.IsReplay);
        Assert.Equal(first.Data!.SaleId, second.Data.SaleId);

        var saleCount = await ctx.Sales.CountAsync(s => s.Id == first.Data.SaleId);
        Assert.Equal(1, saleCount);
        var moveCount = await ctx.StockMovements.CountAsync(m =>
            m.Reason == StockMovementReasons.Sale && m.ProductId == product.Id);
        Assert.Equal(1, moveCount);
    }

    [Fact]
    public async Task Mixed_MembershipAndRetail_CreatesBoth()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);
        var plan = SeedPlan(ctx, tenantId, 200m);
        var member = SeedMember(ctx, tenantId);
        var (product, warehouse) = await SeedStockedProductAsync(ctx, tenantId, qty: 3, sellPrice: 50m);
        await ctx.SaveChangesAsync();

        var result = await svc.CreateSaleAsync(new CreateSaleRequest
        {
            MemberId = member.Id,
            Lines = new List<CreateSaleLineRequest>
            {
                new() { LineType = "membership", PlanId = plan.Id, Qty = 1 },
                new() { LineType = "retail", ProductId = product.Id, Qty = 1 }
            },
            Payments = new List<SalePaymentRequest> { new() { Method = "cash", Amount = 250m } }
        }, identityUserId, tenantId, new HashSet<string>());

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Data!.MembershipId);

        var lines = await ctx.SaleLines.Where(l => l.SaleId == result.Data.SaleId).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.LineType == "membership");
        Assert.Contains(lines, l => l.LineType == "retail" && l.ReferenceId == product.Id);

        var onHand = await ctx.StockBalances.SingleAsync(b =>
            b.ProductId == product.Id && b.WarehouseId == warehouse.Id);
        Assert.Equal(2, onHand.QtyOnHand);

        var payment = await ctx.PaymentTransactions.SingleAsync(p => p.SaleId == result.Data.SaleId);
        Assert.Equal(result.Data.MembershipId, payment.MembershipId);
        Assert.Equal(member.Id, payment.MemberId);
    }

    [Fact]
    public async Task Retail_ExpiredOnly_ReturnsStockUnsellableExpired()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);

        var today = GMS.Core.Utilities.MembershipOperational.TodayCairo();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Sku = "EXP-SKU",
            Name = "Expired Whey",
            UnitOfMeasure = "pcs",
            SellPrice = 100m,
            CostPrice = 40m,
            Currency = "EGP",
            TrackStock = true,
            TrackBatch = true,
            TrackExpiry = true,
            IsActive = true,
            IsSellable = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "MAIN",
            Name = "Main",
            IsDefault = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var batch = new ProductBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            BatchNumber = "OLD",
            ExpiresOn = today.AddDays(-2),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-40)
        };
        ctx.Products.Add(product);
        ctx.Warehouses.Add(warehouse);
        ctx.ProductBatches.Add(batch);
        await ctx.SaveChangesAsync();

        var ledger = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);
        var post = await ledger.PostAsync(new GMS.Application.DTOs.Inventory.StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            BatchId = batch.Id,
            QtyDelta = 4,
            UnitCost = 40m,
            Reason = StockMovementReasons.PurchaseReceipt,
            ReferenceType = "GRN",
            ReferenceId = Guid.NewGuid()
        });
        Assert.True(post.IsSuccess, post.Error);

        var result = await svc.CreateSaleAsync(new CreateSaleRequest
        {
            Lines = new List<CreateSaleLineRequest>
            {
                new() { LineType = "retail", ProductId = product.Id, Qty = 1 }
            },
            Payments = new List<SalePaymentRequest> { new() { Method = "cash", Amount = 100m } }
        }, identityUserId, tenantId, new HashSet<string>());

        Assert.False(result.IsSuccess);
        Assert.StartsWith(SaleFailureReasons.StockUnsellableExpired + "|", result.Error);
    }

    [Fact]
    public async Task Retail_Fefo_PostsEarliestExpiryBatch()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedOpenShift(ctx, tenantId, staff.Id);

        var today = GMS.Core.Utilities.MembershipOperational.TodayCairo();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Sku = "FEFO-SKU",
            Name = "Whey",
            UnitOfMeasure = "pcs",
            SellPrice = 100m,
            CostPrice = 40m,
            Currency = "EGP",
            TrackStock = true,
            TrackBatch = true,
            TrackExpiry = true,
            IsActive = true,
            IsSellable = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "MAIN",
            Name = "Main",
            IsDefault = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var later = new ProductBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            BatchNumber = "LATER",
            ExpiresOn = today.AddDays(90),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30)
        };
        var sooner = new ProductBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            BatchNumber = "SOON",
            ExpiresOn = today.AddDays(14),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        };
        ctx.Products.Add(product);
        ctx.Warehouses.Add(warehouse);
        ctx.ProductBatches.AddRange(later, sooner);
        await ctx.SaveChangesAsync();

        var ledger = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);
        foreach (var (batchId, qty) in new[] { (later.Id, 5m), (sooner.Id, 5m) })
        {
            var post = await ledger.PostAsync(new GMS.Application.DTOs.Inventory.StockLedgerPostRequest
            {
                TenantId = tenantId,
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                BatchId = batchId,
                QtyDelta = qty,
                UnitCost = 40m,
                Reason = StockMovementReasons.PurchaseReceipt,
                ReferenceType = "GRN",
                ReferenceId = Guid.NewGuid()
            });
            Assert.True(post.IsSuccess, post.Error);
        }

        var result = await svc.CreateSaleAsync(new CreateSaleRequest
        {
            Lines = new List<CreateSaleLineRequest>
            {
                new() { LineType = "retail", ProductId = product.Id, Qty = 2 }
            },
            Payments = new List<SalePaymentRequest> { new() { Method = "cash", Amount = 200m } }
        }, identityUserId, tenantId, new HashSet<string>());

        Assert.True(result.IsSuccess, result.Error);
        var line = await ctx.SaleLines.SingleAsync(l => l.SaleId == result.Data!.SaleId);
        var movements = await ctx.StockMovements
            .Where(m => m.ReferenceId == line.Id && m.Reason == StockMovementReasons.Sale)
            .ToListAsync();
        Assert.Single(movements);
        Assert.Equal(sooner.Id, movements[0].BatchId);
        Assert.Equal(-2m, movements[0].QtyDelta);

        Assert.Equal(3m, (await ledger.GetOnHandAsync(tenantId, product.Id, warehouse.Id, sooner.Id)).Data);
        Assert.Equal(5m, (await ledger.GetOnHandAsync(tenantId, product.Id, warehouse.Id, later.Id)).Data);
        Assert.Equal(8m, (await ledger.GetAvailableAsync(tenantId, product.Id, warehouse.Id)).Data);
    }

    [Fact]
    public async Task RecordPayment_TwoConcurrentDebtorPayments_CannotOverAllocateSale()
    {
        var tenantId = Guid.NewGuid();
        var first = CreateSut(useLocalDb: true, tenantIdOverride: tenantId);
        var second = CreateSut(useLocalDb: true, tenantIdOverride: tenantId);
        try
        {
            SeedTenant(first.ctx, tenantId);
            var (staffA, identityA) = SeedStaff(first.ctx, tenantId);
            var (staffB, identityB) = SeedStaff(first.ctx, tenantId);
            SeedOpenShift(first.ctx, tenantId, staffA.Id);
            SeedOpenShift(first.ctx, tenantId, staffB.Id);
            var sale = new Sale
            {
                TenantId = tenantId,
                SoldByUserId = staffA.Id,
                Total = 1000m,
                AmountDue = 1000m,
                Status = "partially_paid"
            };
            first.ctx.Sales.Add(sale);
            await first.ctx.SaveChangesAsync();

            var results = await Task.WhenAll(
                first.svc.RecordPaymentAsync(
                    sale.Id, tenantId, identityA,
                    new RecordPaymentRequest { Amount = 700m, Method = "cash" }),
                second.svc.RecordPaymentAsync(
                    sale.Id, tenantId, identityB,
                    new RecordPaymentRequest { Amount = 700m, Method = "cash" }));

            var paid = await first.ctx.PaymentTransactions
                .Where(item => item.TenantId == tenantId && item.SaleId == sale.Id)
                .SumAsync(item => item.Amount);
            var reloaded = await first.ctx.Sales.SingleAsync(item => item.Id == sale.Id);
            Assert.InRange(paid, 0m, 1000m);
            Assert.True(reloaded.AmountDue >= 0m);
            Assert.True(results.Count(result => result.IsSuccess) <= 1);
        }
        finally
        {
            await first.ctx.CashMovements.Where(item => item.TenantId == tenantId).ExecuteDeleteAsync();
            await first.ctx.PaymentTransactions.Where(item => item.TenantId == tenantId).ExecuteDeleteAsync();
            await first.ctx.Shifts.Where(item => item.TenantId == tenantId).ExecuteDeleteAsync();
            await first.ctx.Sales.Where(item => item.TenantId == tenantId).ExecuteDeleteAsync();
            await first.ctx.AppUsers.Where(item => item.TenantId == tenantId).ExecuteDeleteAsync();
            await first.ctx.Tenants.Where(item => item.Id == tenantId).ExecuteDeleteAsync();
        }
    }

    private static async Task<(Product product, Warehouse warehouse)> SeedStockedProductAsync(
        GymFlowProDbContext ctx, Guid tenantId, decimal qty, decimal sellPrice = 100m)
    {
        var product = new Product
        {
            TenantId = tenantId,
            Sku = $"SKU-{Guid.NewGuid():N}"[..12],
            Name = "Protein",
            UnitOfMeasure = "pcs",
            SellPrice = sellPrice,
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

        var ledger = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);
        var post = await ledger.PostAsync(new GMS.Application.DTOs.Inventory.StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            QtyDelta = qty,
            UnitCost = 40m,
            Reason = StockMovementReasons.Opening,
            ReferenceType = "TestSeed",
            ReferenceId = Guid.NewGuid()
        });
        Assert.True(post.IsSuccess, post.Error);
        return (product, warehouse);
    }
}
