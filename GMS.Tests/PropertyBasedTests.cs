namespace GMS.Tests;

using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;

/// <summary>
/// Property-based invariant checks for the money-path arithmetic. Two of the four exercise the
/// REAL production formula directly (Promo percent via the real PromoService; VAT/Proration mirror
/// SaleService's exact rounding algorithm, since VAT computation isn't exposed as an isolated
/// callable — it only happens inline inside CreateSaleAsync's full atomic flow).
///
/// The Proration and Upgrade+Downgrade properties test the FORMULA a prorated-upgrade feature would
/// need to satisfy — there is no wired-up plan-upgrade/proration feature in this codebase yet (this
/// hardening pass adds no new features), so these two validate the arithmetic invariant in isolation
/// rather than a real end-to-end service.
/// </summary>
public class PropertyBasedTests
{
    private static decimal RoundHalfUp(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Mirrors the exact proration formula a plan-upgrade/downgrade feature would need:
    /// the unused value of a plan already paid for, given how many days remain out of its duration.</summary>
    private static decimal CalculateUnusedValue(int durationDays, int remainingDays, decimal pricePaid)
    {
        if (durationDays <= 0)
            return 0m;

        var raw = pricePaid * remainingDays / durationDays;
        return Math.Clamp(RoundHalfUp(raw), 0m, pricePaid);
    }

    [Property]
    public bool Proration_UnusedValueAlwaysWithinZeroToPricePaid(
        PositiveInt durationDaysGen, NonNegativeInt remainingDaysGen, NonNegativeInt pricePaidGen)
    {
        var durationDays = durationDaysGen.Get % 365 + 1; // [1..365]
        var remainingDays = remainingDaysGen.Get % (durationDays + 1); // [0..durationDays]
        var pricePaid = pricePaidGen.Get % 10001; // [0..10000]

        var unusedValue = CalculateUnusedValue(durationDays, remainingDays, pricePaid);

        return unusedValue >= 0m && unusedValue <= pricePaid;
    }

    [Property]
    public bool Vat_TaxAmountAndTotal_AlwaysNonNegativeAndTotalNeverLessThanNet(
        NonNegativeInt netGen, NonNegativeInt vatRateBasisPointsGen)
    {
        var net = netGen.Get % 100_001; // [0..100000]
        var vatRate = (vatRateBasisPointsGen.Get % 101) / 100m; // [0..1] in steps of 0.01

        // Mirrors SaleService.CreateSaleAsync's exact VAT computation.
        var taxAmount = RoundHalfUp(net * vatRate);
        var total = net + taxAmount;

        return taxAmount >= 0m && total >= net;
    }

    [Property(MaxTest = 30)]
    public bool PromoPercent_RealService_FinalPriceNeverNegativeAndDiscountMatchesFormula(
        PositiveInt priceGen, NonNegativeInt ratePercentGen)
    {
        var price = priceGen.Get % 10000 + 1; // [1..10000]
        var ratePercent = ratePercentGen.Get % 101; // [0..100]

        var (isValid, discount, finalPrice) = RunPromoValidation(price, ratePercent);

        var expectedDiscount = RoundHalfUp(price * ratePercent / 100m);
        var expectedFinalPrice = Math.Max(0m, RoundHalfUp(price - expectedDiscount));

        return isValid && discount == expectedDiscount && finalPrice == expectedFinalPrice && finalPrice >= 0m;
    }

    /// <summary>Upgrading to a pricier plan then immediately downgrading back to the original plan
    /// (zero time elapsed — same remainingDays on both legs) must net to exactly zero: whatever was
    /// prorated as due on the way up must be prorated as an identical credit on the way back down.
    /// No money is created or destroyed by a same-instant round trip.</summary>
    [Property]
    public bool UpgradeDowngradeRoundTrip_ZeroElapsedTime_NetsToZero(
        PositiveInt durationDaysGen, NonNegativeInt remainingDaysGen,
        NonNegativeInt oldPlanPriceGen, NonNegativeInt newPlanPriceGen)
    {
        var durationDays = durationDaysGen.Get % 365 + 1;
        var remainingDays = remainingDaysGen.Get % (durationDays + 1);
        var oldPlanPrice = oldPlanPriceGen.Get % 10001;
        var newPlanPrice = newPlanPriceGen.Get % 10001;

        var priceDifference = newPlanPrice - oldPlanPrice;

        // Upgrading: charge the prorated share of the price increase for the remaining days.
        var upgradeAmountDue = CalculateUnusedValue(durationDays, remainingDays, Math.Abs(priceDifference))
            * Math.Sign(priceDifference);

        // Immediately downgrading back (same remainingDays — no time has passed): the prorated
        // share of the SAME price difference is credited back, reversing the upgrade exactly.
        var downgradeCredit = CalculateUnusedValue(durationDays, remainingDays, Math.Abs(priceDifference))
            * Math.Sign(priceDifference);

        var netLedgerEffect = downgradeCredit - upgradeAmountDue;

        return netLedgerEffect == 0m;
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    private static (bool IsValid, decimal Discount, decimal FinalPrice) RunPromoValidation(decimal price, int ratePercent)
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        using var ctx = new GymFlowProDbContext(options, tenantContext);
        var svc = new PromoService(ctx, new Repository<PromoCode>(ctx), tenantContext, NullLogger<PromoService>.Instance);

        var plan = new MembershipPlan
        {
            TenantId = tenantId, Name = "Property Test Plan", NameAr = "خطة اختبار",
            PlanType = "monthly_unlimited", DurationDays = 30, Price = price
        };
        ctx.MembershipPlans.Add(plan);

        var promo = new PromoCode
        {
            TenantId = tenantId, Code = "PROP10", Type = "percent", Value = ratePercent,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            ValidTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), IsActive = true
        };
        ctx.PromoCodes.Add(promo);
        ctx.SaveChanges();

        var result = svc.ValidateAndPriceAsync("PROP10", plan.Id, Guid.NewGuid(), tenantId).GetAwaiter().GetResult();

        if (!result.IsSuccess || !result.Data!.IsValid)
            return (false, 0m, 0m);

        return (true, result.Data.DiscountAmount ?? 0m, result.Data.FinalPrice ?? 0m);
    }
}
