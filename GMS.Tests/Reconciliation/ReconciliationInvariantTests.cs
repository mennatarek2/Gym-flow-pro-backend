namespace GMS.Tests.Reconciliation;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Common;
using GMS.Application.DTOs.Sales;
using GMS.Application.DTOs.Trials;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;
using GMS.Tests.Helpers;

/// <summary>
/// Seeds a synthetic "busy day" of real service calls (SaleService, ShiftService, RefundService,
/// TrialService — no mocked business logic) across two tenants on real LocalDB, then asserts the
/// four money-path invariants a full reconciliation report depends on. Runs against LocalDB because
/// promo consumption, invoice numbering, and the account-credit balance check all use raw SQL /
/// explicit transactions that EF Core's InMemory provider cannot execute.
///
/// This exact exercise found and fixed three real bugs before the invariants would balance:
///   1. RefundService issued a legal credit note for 'credit'-method refunds too (which don't
///      reverse revenue) — see bug-055.
///   2. InvoiceService.CreateCreditNoteAsync stored a negative Total, breaking the standard
///      invoice-minus-credit-note netting convention — see bug-055.
///   3. SaleService.RecordPaymentAsync never recorded a cash movement for a cash debt payment,
///      silently under-counting the shift's expected drawer cash — found via invariant (b).
/// </summary>
public class ReconciliationInvariantTests
{
    private const string LocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;";

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
        public Task<string> CreatePaymentIntentAsync(Guid membershipId, decimal amount, string memberPhone) => Task.FromResult(string.Empty);
        public bool VerifyWebhookSignature(byte[] body, string hmacHeader) => true;
        public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);
    }

    private class NoOpFawryService : IFawryService
    {
        public Task<string> CreateOrderAsync(Guid membershipId, decimal amount) => Task.FromResult(string.Empty);
        public bool VerifyWebhookSignature(byte[] body, string signature) => true;
        public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);
    }

    private class NoOpOtpSender : IOtpSender
    {
        public Task SendOtpAsync(string phoneNumber, string otp) => Task.CompletedTask;
    }

    private class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public byte[]? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }

    static ReconciliationInvariantTests()
    {
        // InvoiceService.EnqueueForSale / RefundService's post-commit steps call the static
        // Hangfire BackgroundJob.Enqueue directly — needs some JobStorage configured to not throw.
        Hangfire.JobStorage.Current = new Hangfire.InMemory.InMemoryStorage();
    }

    private class TenantFixture
    {
        public Guid TenantId;
        public AppUser Owner = null!;
        public Guid OwnerIdentityId;
        public MembershipPlan MonthlyPlan = null!;
        public MembershipPlan PremiumPlan = null!;
        public MembershipPlan DayPassPlan = null!;
        public MembershipPlan TrialPlan = null!;
    }

    private class Services
    {
        public SaleService SaleService = null!;
        public ShiftService ShiftService = null!;
        public RefundService RefundService = null!;
        public TrialService TrialService = null!;
        public IOtpService OtpService = null!;
        public InvoiceService InvoiceService = null!;
    }

    [Fact]
    public async Task BusyDay_TwoTenants_AllMoneyPathInvariantsHold()
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>().UseSqlServer(LocalDbConnectionString).Options;
        var tenantContext = new TenantContext();
        var ctx = new GymFlowProDbContext(options, tenantContext);

        var tenantIdA = Guid.NewGuid();
        var tenantIdB = Guid.NewGuid();

        try
        {
            var fixtureA = await SeedTenantAsync(ctx, tenantContext, tenantIdA, "Recon Tenant A");
            var fixtureB = await SeedTenantAsync(ctx, tenantContext, tenantIdB, "Recon Tenant B");

            await RunTenantABusyDayAsync(ctx, tenantContext, fixtureA);
            await RunTenantBBusyDayAsync(ctx, tenantContext, fixtureB);

            await AssertInvariantsAsync(ctx, tenantIdA);
            await AssertInvariantsAsync(ctx, tenantIdB);
            await AssertNoCrossTenantLeakageAsync(ctx, tenantIdA, tenantIdB);
        }
        finally
        {
            await CleanupTenantAsync(ctx, tenantIdA);
            await CleanupTenantAsync(ctx, tenantIdB);
        }
    }

    // ========================================================================
    // TENANT A: 3 full-pay cash sales (promo, manual discount), partial-pay + debt
    // payment, day-pass, trial, plan upgrade, cash refund, credit refund.
    // ========================================================================

    private static async Task RunTenantABusyDayAsync(GymFlowProDbContext ctx, ITenantContext tenantContext, TenantFixture fx)
    {
        tenantContext.SetTenant(fx.TenantId, "Recon Tenant A", "Africa/Cairo");
        var svc = BuildServices(ctx, tenantContext);

        var openResult = await svc.ShiftService.OpenAsync(0m, fx.OwnerIdentityId, fx.TenantId);
        Assert.True(openResult.IsSuccess, openResult.Error);

        // Sale 1: full cash, no promo/discount — 500 EGP.
        var sale1 = await CreateSaleAsync(svc, fx.TenantId, fx.OwnerIdentityId, fx.MonthlyPlan.Id, "+201000000101", ("cash", 500m));

        // Sale 2: full cash, WITH a 10% promo — 500 -> 450.
        ctx.PromoCodes.Add(new PromoCode
        {
            TenantId = fx.TenantId, Code = "RECON10", Type = "percent", Value = 10m,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), ValidTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            IsActive = true
        });
        await ctx.SaveChangesAsync();
        var sale2 = await CreateSaleAsync(svc, fx.TenantId, fx.OwnerIdentityId, fx.MonthlyPlan.Id, "+201000000102", ("cash", 450m), promoCode: "RECON10");

        // Sale 3: full cash, WITH a manual discount override — 500 -> 450.
        var sale3 = await CreateSaleAsync(svc, fx.TenantId, fx.OwnerIdentityId, fx.MonthlyPlan.Id, "+201000000103", ("cash", 450m),
            manualDiscount: 50m, callerPermissions: new HashSet<string> { Permissions.SalesDiscountOverride });

        // Sale 4: partial-pay (200 of 500) then a debt payment for the remaining 300 (P13's flow).
        var sale4 = await CreateSaleAsync(svc, fx.TenantId, fx.OwnerIdentityId, fx.MonthlyPlan.Id, "+201000000104", ("cash", 200m), partialPay: true);
        var debtPaymentResult = await svc.SaleService.RecordPaymentAsync(
            sale4.SaleId, fx.TenantId, fx.OwnerIdentityId, new RecordPaymentRequest { Method = "cash", Amount = 300m });
        Assert.True(debtPaymentResult.IsSuccess, debtPaymentResult.Error);

        // Sale 5: day-pass, full cash — 100 EGP.
        var sale5 = await CreateSaleAsync(svc, fx.TenantId, fx.OwnerIdentityId, fx.DayPassPlan.Id, "+201000000105", ("cash", 100m));

        // Trial: two-step OTP-verified issuance (Total = 0, no PaymentTransaction/invoice expected).
        const string trialPhone = "+201000000199";
        var initiateResult = await svc.TrialService.InitiateAsync(
            new TrialInitiateRequest { FullName = "Trial Member", PhoneNumber = trialPhone, PlanId = fx.TrialPlan.Id }, fx.TenantId);
        Assert.True(initiateResult.IsSuccess, initiateResult.Error);

        var otp = await svc.OtpService.GenerateOtpAsync(trialPhone, fx.TenantId); // overwrites with a known value
        var confirmResult = await svc.TrialService.ConfirmAsync(
            new TrialConfirmRequest { PhoneNumber = trialPhone, Otp = otp }, fx.OwnerIdentityId, fx.TenantId);
        Assert.True(confirmResult.IsSuccess, confirmResult.Error);

        // "Plan upgrade": the sale-1 member buys a higher tier plan on top of their existing one.
        // (No standalone prorated-upgrade feature exists yet — see PropertyBasedTests' doc comment —
        // so this models "upgrading" as a second real sale, keeping every EGP flowing through the
        // same, already-reconciliation-consistent SaleService path.)
        var sale1Member = await ctx.Sales.Where(s => s.Id == sale1.SaleId).Select(s => s.MemberId).FirstAsync();
        var sale6 = await CreateSaleAsync(svc, fx.TenantId, fx.OwnerIdentityId, fx.PremiumPlan.Id, memberId: sale1Member!.Value, ("cash", 800m));

        // Drain the invoice queue now (see DrainInvoiceQueueAsync's doc comment) so the refunds
        // below can link their credit notes to a real original invoice.
        await DrainInvoiceQueueAsync(ctx, svc.InvoiceService, fx.TenantId);

        // Cash refund: partial refund of 100 against sale 1.
        var cashRefundRequest = await svc.RefundService.RequestAsync(sale1.SaleId, 100m, "cash", "Customer complaint", fx.OwnerIdentityId, fx.TenantId);
        Assert.True(cashRefundRequest.IsSuccess, cashRefundRequest.Error);
        var cashRefundApprove = await svc.RefundService.ApproveAsync(cashRefundRequest.Data!.Id, fx.OwnerIdentityId, fx.TenantId);
        Assert.True(cashRefundApprove.IsSuccess, cashRefundApprove.Error);

        // Credit refund: partial refund of 50 against sale 2 — store credit, not cash back.
        var creditRefundRequest = await svc.RefundService.RequestAsync(sale2.SaleId, 50m, "credit", "Goodwill credit", fx.OwnerIdentityId, fx.TenantId);
        Assert.True(creditRefundRequest.IsSuccess, creditRefundRequest.Error);
        var creditRefundApprove = await svc.RefundService.ApproveAsync(creditRefundRequest.Data!.Id, fx.OwnerIdentityId, fx.TenantId);
        Assert.True(creditRefundApprove.IsSuccess, creditRefundApprove.Error);

        // Close the shift so ExpectedCash gets computed (invariant b needs it populated).
        var movementsSum = await ctx.CashMovements.Where(c => c.TenantId == fx.TenantId).SumAsync(c => c.Amount);
        var closeResult = await svc.ShiftService.CloseAsync(movementsSum, null, fx.OwnerIdentityId, fx.TenantId);
        Assert.True(closeResult.IsSuccess, closeResult.Error);
    }

    // ========================================================================
    // TENANT B: 2 sales, 1 refund — small fixture whose only purpose is proving
    // Tenant A's data never leaks into it (and vice versa).
    // ========================================================================

    private static async Task RunTenantBBusyDayAsync(GymFlowProDbContext ctx, ITenantContext tenantContext, TenantFixture fx)
    {
        tenantContext.SetTenant(fx.TenantId, "Recon Tenant B", "Africa/Cairo");
        var svc = BuildServices(ctx, tenantContext);

        var openResult = await svc.ShiftService.OpenAsync(0m, fx.OwnerIdentityId, fx.TenantId);
        Assert.True(openResult.IsSuccess, openResult.Error);

        var saleB1 = await CreateSaleAsync(svc, fx.TenantId, fx.OwnerIdentityId, fx.MonthlyPlan.Id, "+201000000201", ("cash", 500m));
        var saleB2 = await CreateSaleAsync(svc, fx.TenantId, fx.OwnerIdentityId, fx.MonthlyPlan.Id, "+201000000202", ("cash", 500m));

        await DrainInvoiceQueueAsync(ctx, svc.InvoiceService, fx.TenantId);

        var refundRequest = await svc.RefundService.RequestAsync(saleB1.SaleId, 200m, "cash", "Refund", fx.OwnerIdentityId, fx.TenantId);
        Assert.True(refundRequest.IsSuccess, refundRequest.Error);
        var refundApprove = await svc.RefundService.ApproveAsync(refundRequest.Data!.Id, fx.OwnerIdentityId, fx.TenantId);
        Assert.True(refundApprove.IsSuccess, refundApprove.Error);

        var movementsSum = await ctx.CashMovements.Where(c => c.TenantId == fx.TenantId).SumAsync(c => c.Amount);
        var closeResult = await svc.ShiftService.CloseAsync(movementsSum, null, fx.OwnerIdentityId, fx.TenantId);
        Assert.True(closeResult.IsSuccess, closeResult.Error);
    }

    // ========================================================================
    // INVARIANT ASSERTIONS
    // ========================================================================

    private static async Task AssertInvariantsAsync(GymFlowProDbContext ctx, Guid tenantId)
    {
        var saleIds = await ctx.Sales.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.Id)
            .ToListAsync();

        // (a) Net revenue: successful payments minus non-credit refunds must equal invoices minus credit notes.
        var paymentsSum = await ctx.PaymentTransactions.IgnoreQueryFilters()
            .Where(p => p.SaleId != null && saleIds.Contains(p.SaleId.Value) && p.Status == "success")
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var nonCreditRefundsSum = await ctx.Refunds.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.Status == "executed" && r.Method != "credit")
            .SumAsync(r => (decimal?)r.Amount) ?? 0m;

        var invoiceTotalsSum = await ctx.Invoices.IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && i.Type == "invoice")
            .SumAsync(i => (decimal?)i.Total) ?? 0m;

        var creditNoteTotalsSum = await ctx.Invoices.IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && i.Type == "credit_note")
            .SumAsync(i => (decimal?)i.Total) ?? 0m;

        Assert.Equal(paymentsSum - nonCreditRefundsSum, invoiceTotalsSum - creditNoteTotalsSum);

        // (b) Per shift: the sum of cash movements must equal ExpectedCash - OpeningFloat.
        var shifts = await ctx.Shifts.IgnoreQueryFilters()
            .Include(s => s.Movements)
            .Where(s => s.TenantId == tenantId && s.ExpectedCash != null)
            .ToListAsync();

        Assert.NotEmpty(shifts);
        foreach (var shift in shifts)
        {
            var movementsSum = shift.Movements.Sum(m => m.Amount);
            Assert.Equal(movementsSum, shift.ExpectedCash!.Value - shift.OpeningFloat);
        }

        // (c) Per member: the account-credit ledger balance must never go negative.
        var memberBalances = await ctx.MemberCredits.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .GroupBy(c => c.MemberId)
            .Select(g => new { MemberId = g.Key, Balance = g.Sum(c => c.Amount) })
            .ToListAsync();

        foreach (var balance in memberBalances)
            Assert.True(balance.Balance >= 0m, $"Member {balance.MemberId} has a negative credit balance: {balance.Balance}");

        // (d) Per sale: AmountDue must always equal Total minus successfully-received payments.
        var sales = await ctx.Sales.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).ToListAsync();
        foreach (var sale in sales)
        {
            var paidForSale = await ctx.PaymentTransactions.IgnoreQueryFilters()
                .Where(p => p.SaleId == sale.Id && p.Status == "success")
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            Assert.Equal(Math.Max(0m, sale.Total - paidForSale), sale.AmountDue);
        }
    }

    private static async Task AssertNoCrossTenantLeakageAsync(GymFlowProDbContext ctx, Guid tenantIdA, Guid tenantIdB)
    {
        var salesA = await ctx.Sales.IgnoreQueryFilters().Where(s => s.TenantId == tenantIdA).Select(s => s.Id).ToListAsync();
        var salesB = await ctx.Sales.IgnoreQueryFilters().Where(s => s.TenantId == tenantIdB).Select(s => s.Id).ToListAsync();
        Assert.Empty(salesA.Intersect(salesB));

        var crossTenantPayments = await ctx.PaymentTransactions.IgnoreQueryFilters()
            .Where(p => p.SaleId != null && salesA.Contains(p.SaleId.Value) && p.TenantId == tenantIdB)
            .CountAsync();
        Assert.Equal(0, crossTenantPayments);

        var membersA = await ctx.GymMembers.IgnoreQueryFilters().Where(m => m.TenantId == tenantIdA).Select(m => m.Id).ToListAsync();
        var membersB = await ctx.GymMembers.IgnoreQueryFilters().Where(m => m.TenantId == tenantIdB).Select(m => m.Id).ToListAsync();
        Assert.Empty(membersA.Intersect(membersB));
    }

    // ========================================================================
    // SEEDING / SERVICE CONSTRUCTION HELPERS
    // ========================================================================

    private static async Task<TenantFixture> SeedTenantAsync(
        GymFlowProDbContext ctx, ITenantContext tenantContext, Guid tenantId, string name)
    {
        tenantContext.SetTenant(tenantId, name, "Africa/Cairo");

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = name, NameAr = name, GymCode = $"GYM-{tenantId:N}".Substring(0, 13),
            City = "Cairo", Address = "Test Address", PhoneNumber = "0100000000",
            Email = $"{tenantId}@test.local", IsActive = true, SubscriptionStartDate = DateTime.UtcNow
        });

        var ownerIdentityId = Guid.NewGuid();
        var owner = new AppUser
        {
            TenantId = tenantId, UserId = ownerIdentityId.ToString(), FirstName = "Owner", LastName = name,
            Email = $"owner-{ownerIdentityId}@test.local", Role = "Owner"
        };
        ctx.AppUsers.Add(owner);

        var monthly = new MembershipPlan
        {
            TenantId = tenantId, Name = "Monthly Unlimited", NameAr = "شهري بدون حدود",
            PlanType = "monthly_unlimited", DurationDays = 30, Price = 500m, IsActive = true
        };
        var premium = new MembershipPlan
        {
            TenantId = tenantId, Name = "Premium Monthly", NameAr = "شهري مميز",
            PlanType = "monthly_unlimited", DurationDays = 30, Price = 800m, IsActive = true
        };
        var dayPass = new MembershipPlan
        {
            TenantId = tenantId, Name = "Day Pass", NameAr = "تذكرة يوم",
            PlanType = "day_pass", DurationDays = 1, Price = 100m, IsActive = true
        };
        var trial = new MembershipPlan
        {
            TenantId = tenantId, Name = "Free Trial", NameAr = "تجربة مجانية",
            PlanType = "trial", DurationDays = 7, Price = 0m, IsActive = true
        };
        ctx.MembershipPlans.AddRange(monthly, premium, dayPass, trial);

        await ctx.SaveChangesAsync();

        return new TenantFixture
        {
            TenantId = tenantId, Owner = owner, OwnerIdentityId = ownerIdentityId,
            MonthlyPlan = monthly, PremiumPlan = premium, DayPassPlan = dayPass, TrialPlan = trial
        };
    }

    private static Services BuildServices(GymFlowProDbContext ctx, ITenantContext tenantContext)
    {
        var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var shiftService = new ShiftService(ctx, auditService, NullLogger<ShiftService>.Instance);
        var invoiceService = new InvoiceService(ctx, auditService, NullLogger<InvoiceService>.Instance);
        var memberService = new MemberService(
            ctx, new MemberRepository(ctx), new AesEncryptionService(new ConfigurationBuilder().Build()),
            new UnlimitedTierEnforcement(),
            new NoOpReferralAttribution(),
            new NoOpMemberAppActivation(),
            new ActivityEntitlementService(ctx),
            NullLogger<MemberService>.Instance);
        var promoService = new PromoService(ctx, new Repository<PromoCode>(ctx), tenantContext, NullLogger<PromoService>.Instance);

        var saleService = new SaleService(
            ctx, memberService, promoService, auditService, shiftService, invoiceService, new NoOpWhatsAppService(), new NoOpReferralAttribution(),
            new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance),
            new AlwaysEnabledFeatureAccess(),
            NullLogger<SaleService>.Instance);

        var refundService = new RefundService(
            ctx, shiftService, invoiceService, new NoOpWhatsAppService(),
            new NoOpPaymobService(), new NoOpFawryService(), auditService,
            new NoOpReferralRewardService(),
            new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance),
            NullLogger<RefundService>.Instance);

        var otpService = new OtpService(new MemoryCache(new MemoryCacheOptions()), new NoOpOtpSender(), NullLogger<OtpService>.Instance);
        var trialService = new TrialService(
            ctx, new MemberRepository(ctx), otpService, new FakeDistributedCache(), invoiceService, new UnlimitedTierEnforcement(), NullLogger<TrialService>.Instance);

        return new Services
        {
            SaleService = saleService, ShiftService = shiftService, RefundService = refundService,
            TrialService = trialService, OtpService = otpService, InvoiceService = invoiceService
        };
    }

    /// <summary>
    /// SaleService.CreateSaleAsync only ENQUEUES a Hangfire job (CreateInvoiceForSaleJob) to create
    /// each sale's invoice — nothing in this test process dequeues and runs it (no live Hangfire
    /// worker), so invoices must be created directly here, exactly mirroring what that job's
    /// ExecuteAsync does. CreateForSaleAsync is idempotent (a no-op if the invoice already exists),
    /// so this is safe to call for every sale regardless of total.
    /// </summary>
    private static async Task DrainInvoiceQueueAsync(GymFlowProDbContext ctx, InvoiceService invoiceService, Guid tenantId)
    {
        var saleIds = await ctx.Sales.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).Select(s => s.Id).ToListAsync();
        foreach (var saleId in saleIds)
            await invoiceService.CreateForSaleAsync(saleId);
    }

    private static async Task<SaleResponse> CreateSaleAsync(
        Services svc, Guid tenantId, Guid staffIdentityId, Guid planId, string newMemberPhone,
        (string Method, decimal Amount) payment,
        string? promoCode = null, decimal? manualDiscount = null, bool partialPay = false,
        IReadOnlySet<string>? callerPermissions = null)
    {
        var request = new CreateSaleRequest
        {
            PlanId = planId,
            NewMember = new NewMemberRequest { FullName = "Recon Member", FullNameAr = "عضو", PhoneNumber = newMemberPhone },
            PromoCode = promoCode,
            Payments = new List<SalePaymentRequest> { new() { Method = payment.Method, Amount = payment.Amount } }
        };

        if (manualDiscount.HasValue)
            request.ManualDiscount = new ManualDiscountRequest { Amount = manualDiscount.Value, Reason = "loyalty" };

        if (partialPay)
            request.PartialPayment = new PartialPaymentRequest { DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)) };

        var result = await svc.SaleService.CreateSaleAsync(request, staffIdentityId, tenantId, callerPermissions ?? new HashSet<string>());
        Assert.True(result.IsSuccess, result.Error);
        return result.Data!;
    }

    private static async Task<SaleResponse> CreateSaleAsync(
        Services svc, Guid tenantId, Guid staffIdentityId, Guid planId, Guid memberId,
        (string Method, decimal Amount) payment)
    {
        var request = new CreateSaleRequest
        {
            PlanId = planId,
            MemberId = memberId,
            Payments = new List<SalePaymentRequest> { new() { Method = payment.Method, Amount = payment.Amount } }
        };

        var result = await svc.SaleService.CreateSaleAsync(request, staffIdentityId, tenantId, new HashSet<string>());
        Assert.True(result.IsSuccess, result.Error);
        return result.Data!;
    }

    /// <summary>
    /// Uses IgnoreQueryFilters() throughout — this runs for BOTH tenants sequentially against the
    /// SAME shared ctx/tenantContext, and by the time cleanup runs the ambient tenant is whichever
    /// one ran last (Tenant B). Without IgnoreQueryFilters(), the ambient tenant filter ANDs with the
    /// explicit TenantId==tenantId predicate and silently matches zero rows for the OTHER tenant,
    /// leaving orphaned rows that then fail the final Tenants delete with an FK conflict.
    /// </summary>
    private static async Task CleanupTenantAsync(GymFlowProDbContext ctx, Guid tenantId)
    {
        await ctx.AuditEvents.IgnoreQueryFilters().Where(a => a.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.InvoiceSequences.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.Refunds.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.MemberCredits.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.CashMovements.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.Shifts.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
        // payment_transactions references memberships (FK), so payments must go BEFORE memberships.
        await ctx.PaymentTransactions.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.SaleLines.IgnoreQueryFilters().Where(l => l.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.Sales.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.Memberships.IgnoreQueryFilters().Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.GymMembers.IgnoreQueryFilters().Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.PromoCodes.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.MembershipPlans.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.AppUsers.IgnoreQueryFilters().Where(u => u.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
    }
}
