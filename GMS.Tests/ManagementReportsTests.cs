namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class ManagementReportsTests
{
    private static (GymFlowProDbContext ctx, ReportsService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        var svc = new ReportsService(ctx, new NoopInventoryReports(), NullLogger<ReportsService>.Instance);
        return (ctx, svc, tenantId);
    }

    private static void SeedTenant(GymFlowProDbContext ctx, Guid tenantId)
    {
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة اختبار",
            GymCode = $"GYM-{tenantId:N}"[..13],
            City = "Cairo",
            Address = "Test",
            PhoneNumber = "0100000000",
            Email = $"{tenantId}@test.local",
            SubscriptionStartDate = DateTime.UtcNow
        });
    }

    private static Guid SeedStaff(GymFlowProDbContext ctx, Guid tenantId)
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
        return staff.Id;
    }

    private static async Task<PaymentTransaction> SeedPaymentAsync(
        GymFlowProDbContext ctx, Guid tenantId, Guid staffId, DateTime paidAtUtc,
        decimal amount, string method, string status = "success", Guid? shiftId = null)
    {
        var sale = new Sale
        {
            TenantId = tenantId,
            SoldByUserId = staffId,
            Subtotal = 800,
            Total = 800,
            Status = "partially_paid",
            AmountDue = 800 - amount
        };
        ctx.Sales.Add(sale);

        var pay = new PaymentTransaction
        {
            TenantId = tenantId,
            Gateway = method,
            ExternalRef = Guid.NewGuid().ToString(),
            Amount = amount,
            Status = status,
            PaidAtUtc = paidAtUtc,
            SaleId = sale.Id,
            Method = method,
            ReceivedByUserId = staffId,
            ShiftId = shiftId
        };
        ctx.PaymentTransactions.Add(pay);
        await ctx.SaveChangesAsync();
        return pay;
    }

    private static Shift SeedShift(
        GymFlowProDbContext ctx, Guid tenantId, Guid staffId, DateTime openedAt,
        string status = "closed", DateTime? closedAt = null)
    {
        var shift = new Shift
        {
            TenantId = tenantId,
            UserId = staffId,
            OpenedAt = openedAt,
            ClosedAt = closedAt ?? (status == "open" ? null : openedAt.AddHours(8)),
            OpeningFloat = 0m,
            Status = status
        };
        ctx.Shifts.Add(shift);
        return shift;
    }

    [Fact]
    public async Task Sales_UsesPaymentAmount_NotPlanPrice()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, utcEnd) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var paidAt = utcStart.AddHours(10);

        await SeedPaymentAsync(ctx, tenantId, staffId, paidAt, amount: 200m, method: "cash");

        var result = await svc.GetSalesReportAsync(tenantId, day, day);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(200m, result.Data!.CashInTotal);
        Assert.Single(result.Data.Payments);
        Assert.Equal(200m, result.Data.Payments[0].Amount);
    }

    [Fact]
    public async Task Sales_CollectLater_UsesPaidAtUtc_NotSaleCreated()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var paidAt = utcStart.AddHours(8);

        var pay = await SeedPaymentAsync(ctx, tenantId, staffId, paidAt, 150m, "cash");
        var sale = await ctx.Sales.FirstAsync(s => s.Id == pay.SaleId);
        sale.CreatedAtUtc = utcStart.AddDays(-2);
        await ctx.SaveChangesAsync();

        var result = await svc.GetSalesReportAsync(tenantId, day, day);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(150m, result.Data!.CashInTotal);
        Assert.Equal(0m, result.Data.BookedTotal);
    }

    [Fact]
    public async Task Sales_IgnoresFailedPayments_AndOtherTenants()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var paidAt = utcStart.AddHours(4);

        await SeedPaymentAsync(ctx, tenantId, staffId, paidAt, 100m, "cash", status: "failed");
        await SeedPaymentAsync(ctx, tenantId, staffId, paidAt, 80m, "cash");

        var other = Guid.NewGuid();
        ctx.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = other,
            Gateway = "cash",
            ExternalRef = Guid.NewGuid().ToString(),
            Amount = 999m,
            Status = "success",
            PaidAtUtc = paidAt,
            Method = "cash"
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetSalesReportAsync(tenantId, day, day);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(80m, result.Data!.CashInTotal);
    }

    [Fact]
    public async Task Refunds_ExecutedCash_ReducesNet_RequestedIgnored()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(12);

        var pay = await SeedPaymentAsync(ctx, tenantId, staffId, at, 200m, "cash");
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = pay.SaleId!.Value,
            Amount = 50m,
            Method = "cash",
            Reason = "test",
            RequestedByUserId = staffId,
            Status = "executed",
            ExecutedAt = at.AddMinutes(10)
        });
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = pay.SaleId!.Value,
            Amount = 40m,
            Method = "cash",
            Reason = "pending",
            RequestedByUserId = staffId,
            Status = "requested"
        });
        await ctx.SaveChangesAsync();

        var sales = await svc.GetSalesReportAsync(tenantId, day, day);
        Assert.Equal(200m, sales.Data!.CashInTotal);
        Assert.Equal(50m, sales.Data.CashRefundsTotal);
        Assert.Equal(150m, sales.Data.NetCashIn);

        var refunds = await svc.GetRefundsReportAsync(tenantId, day, day);
        Assert.True(refunds.IsSuccess, refunds.Error);
        Assert.Equal(50m, refunds.Data!.Total);
        Assert.Equal(50m, refunds.Data.CashTotal);
        Assert.Single(refunds.Data.Items);
        Assert.Equal(1, refunds.Data.Count);
        Assert.Equal(50m, refunds.Data.Average);
    }

    [Fact]
    public async Task Refunds_StaffFilter_UsesApproverOrRequester()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var a = SeedStaff(ctx, tenantId);
        var b = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(12);

        var payA = await SeedPaymentAsync(ctx, tenantId, a, at, 200m, "cash");
        var payB = await SeedPaymentAsync(ctx, tenantId, b, at, 80m, "cash");
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = payA.SaleId!.Value,
            Amount = 40m,
            Method = "cash",
            Reason = "a",
            RequestedByUserId = a,
            ApprovedByUserId = a,
            Status = "executed",
            ExecutedAt = at.AddMinutes(1)
        });
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = payB.SaleId!.Value,
            Amount = 80m,
            Method = "credit",
            Reason = "b",
            RequestedByUserId = b,
            ApprovedByUserId = b,
            Status = "executed",
            ExecutedAt = at.AddMinutes(2)
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetRefundsReportAsync(tenantId, day, day, staffId: a);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(40m, result.Data!.Total);
        Assert.Equal(1, result.Data.Count);
        Assert.Equal(2, result.Data.Staff.Count);
    }

    [Fact]
    public async Task Refunds_MethodFilter_DoesNotInventPaymentMethods()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(9);

        var pay = await SeedPaymentAsync(ctx, tenantId, staffId, at, 300m, "card_paymob");
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = pay.SaleId!.Value,
            Amount = 50m,
            Method = "credit",
            Reason = "store credit",
            RequestedByUserId = staffId,
            Status = "executed",
            ExecutedAt = at.AddMinutes(4)
        });
        await ctx.SaveChangesAsync();

        var credit = await svc.GetRefundsReportAsync(tenantId, day, day, method: "credit");
        Assert.Equal(50m, credit.Data!.Total);
        Assert.Contains("credit", credit.Data.MethodOptions);

        var cash = await svc.GetRefundsReportAsync(tenantId, day, day, method: "cash");
        Assert.Equal(0m, cash.Data!.Total);
        Assert.Empty(cash.Data.Items);
    }

    [Fact]
    public async Task Refunds_TwoOnSameSale_CountVsSaleCount()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(10);

        var pay = await SeedPaymentAsync(ctx, tenantId, staffId, at, 200m, "cash");
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = pay.SaleId!.Value,
            Amount = 30m,
            Method = "cash",
            Reason = "1",
            RequestedByUserId = staffId,
            Status = "executed",
            ExecutedAt = at.AddMinutes(1)
        });
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = pay.SaleId!.Value,
            Amount = 20m,
            Method = "cash",
            Reason = "2",
            RequestedByUserId = staffId,
            Status = "executed",
            ExecutedAt = at.AddMinutes(2)
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetRefundsReportAsync(tenantId, day, day);
        Assert.Equal(50m, result.Data!.Total);
        Assert.Equal(2, result.Data.Count);
        Assert.Equal(1, result.Data.SaleCount);
        Assert.Equal(25m, result.Data.Average);
    }

    [Fact]
    public async Task Sales_StaffFilter_OnlyThatReceiver()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var a = SeedStaff(ctx, tenantId);
        var b = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(9);

        await SeedPaymentAsync(ctx, tenantId, a, at, 200m, "cash");
        await SeedPaymentAsync(ctx, tenantId, b, at, 80m, "cash");

        var result = await svc.GetSalesReportAsync(tenantId, day, day, staffId: a);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(200m, result.Data!.CashInTotal);
        Assert.Equal(1, result.Data.TransactionCount);
        Assert.Equal(2, result.Data.Staff.Count);
    }

    [Fact]
    public async Task Sales_CardFilter_DoesNotSubtractCashRefunds()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(11);

        var card = await SeedPaymentAsync(ctx, tenantId, staffId, at, 300m, "card_paymob");
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = card.SaleId!.Value,
            Amount = 50m,
            Method = "cash",
            Reason = "test",
            RequestedByUserId = staffId,
            Status = "executed",
            ExecutedAt = at.AddMinutes(5)
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetSalesReportAsync(tenantId, day, day, paymentMethod: "card_paymob");
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(300m, result.Data!.CashInTotal);
        Assert.Equal(0m, result.Data.CashRefundsTotal);
        Assert.Equal(300m, result.Data.NetCashIn);
    }

    [Fact]
    public async Task Sales_ClassifiesMembershipAndRetail_FromSaleLines()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(10);

        var mem = await SeedPaymentAsync(ctx, tenantId, staffId, at, 800m, "cash");
        var prod = await SeedPaymentAsync(ctx, tenantId, staffId, at, 120m, "cash");
        ctx.SaleLines.Add(new SaleLine
        {
            TenantId = tenantId,
            SaleId = mem.SaleId!.Value,
            LineType = "membership",
            Description = "Plan",
            Qty = 1,
            UnitPrice = 800,
            LineTotal = 800
        });
        ctx.SaleLines.Add(new SaleLine
        {
            TenantId = tenantId,
            SaleId = prod.SaleId!.Value,
            LineType = "retail",
            Description = "Water",
            Qty = 2,
            UnitPrice = 60,
            LineTotal = 120
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetSalesReportAsync(tenantId, day, day);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(920m, result.Data!.CashInTotal);
        Assert.Equal(800m, result.Data.MembershipCashIn);
        Assert.Equal(120m, result.Data.ProductCashIn);
        Assert.Equal(2, result.Data.TransactionCount);
        Assert.Equal(result.Data.CashInTotal, result.Data.Payments.Sum(p => p.Amount));

        var onlyMem = await svc.GetSalesReportAsync(tenantId, day, day, saleType: "membership");
        Assert.Equal(800m, onlyMem.Data!.CashInTotal);
        Assert.Single(onlyMem.Data.Payments);
    }

    [Fact]
    public async Task Sales_DiscountDoesNotChangeNetCashIn()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(8);

        var pay = await SeedPaymentAsync(ctx, tenantId, staffId, at, 700m, "cash");
        var sale = await ctx.Sales.FirstAsync(s => s.Id == pay.SaleId);
        sale.DiscountAmount = 50m;
        sale.ManualDiscountAmount = 50m;
        await ctx.SaveChangesAsync();

        var result = await svc.GetSalesReportAsync(tenantId, day, day);
        Assert.Equal(700m, result.Data!.CashInTotal);
        Assert.Equal(700m, result.Data.NetCashIn);
        Assert.Equal(100m, result.Data.DiscountTotal);
    }

    [Fact]
    public async Task Memberships_AssignIsNew_RenewalFlagged_PriceIsNotRevenue()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        var memberId = SeedMember(ctx, tenantId, "Amira");
        var plan = SeedPlan(ctx, tenantId, "Monthly", 800m);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(10);

        var assigned = await SeedMembershipSaleAsync(
            ctx, tenantId, memberId, plan.Id, staffId, day, at,
            cashIn: 200m, amountPaidField: 800m, renewal: false);
        await SeedMembershipSaleAsync(
            ctx, tenantId, memberId, plan.Id, staffId, day, at.AddMinutes(5),
            cashIn: 200m, amountPaidField: 800m, renewal: true);

        var other = Guid.NewGuid();
        ctx.Memberships.Add(new Membership
        {
            TenantId = other,
            MemberId = Guid.NewGuid(),
            PlanId = plan.Id,
            StartDate = day,
            EndDate = day.AddDays(30),
            Status = "active",
            AmountPaid = 999m,
            CreatedAtUtc = at
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetMembershipsReportAsync(tenantId, day, day);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Data!.Started);
        Assert.Equal(1, result.Data.NewCount);
        Assert.Equal(1, result.Data.RenewalCount);
        Assert.Equal(400m, result.Data.Revenue);
        Assert.Equal(0, result.Data.RefundedCount);
        Assert.DoesNotContain(result.Data.StartedRows, r => r.Amount == 800m);

        var assignedRow = result.Data.StartedRows.Single(r => r.Id == assigned.Id);
        Assert.Equal("new", assignedRow.Type);
        Assert.Equal(200m, assignedRow.Amount);
        Assert.Equal("active", assignedRow.Status);

        var onlyNew = await svc.GetMembershipsReportAsync(tenantId, day, day, type: "new");
        Assert.Equal(1, onlyNew.Data!.Started);
        Assert.Equal("new", onlyNew.Data.StartedRows[0].Type);

        var byPlan = await svc.GetMembershipsReportAsync(tenantId, day, day, planId: plan.Id);
        Assert.Equal(2, byPlan.Data!.Started);
        var byStaff = await svc.GetMembershipsReportAsync(tenantId, day, day, staffId: staffId);
        Assert.Equal(2, byStaff.Data!.Started);
    }

    [Fact]
    public async Task Memberships_ExecutedRefund_NotRevenue_DeskCancelIsNotRefunded()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        var memberId = SeedMember(ctx, tenantId, "Omar");
        var plan = SeedPlan(ctx, tenantId, "Monthly", 800m);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(11);

        var refunded = await SeedMembershipSaleAsync(
            ctx, tenantId, memberId, plan.Id, staffId, day, at,
            cashIn: 200m, amountPaidField: 800m, renewal: false);
        refunded.Status = "cancelled";
        refunded.UpdatedAtUtc = at.AddMinutes(20);
        var refundSale = await ctx.Sales.FirstAsync(s => s.Id == ctx.PaymentTransactions.First(p => p.MembershipId == refunded.Id).SaleId);
        refundSale.Status = "refunded";
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = refundSale.Id,
            Amount = 200m,
            Method = "cash",
            Reason = "left gym",
            RequestedByUserId = staffId,
            Status = "executed",
            ExecutedAt = at.AddMinutes(20)
        });

        var deskCancel = await SeedMembershipSaleAsync(
            ctx, tenantId, memberId, plan.Id, staffId, day, at.AddMinutes(1),
            cashIn: 150m, amountPaidField: 150m, renewal: false);
        deskCancel.Status = "cancelled";
        deskCancel.UpdatedAtUtc = at.AddMinutes(30);
        await ctx.SaveChangesAsync();

        var result = await svc.GetMembershipsReportAsync(tenantId, day, day);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Data!.Started);
        Assert.Equal(1, result.Data.RefundedCount);
        Assert.Equal(150m, result.Data.Revenue);

        var refundRow = result.Data.StartedRows.Single(r => r.Id == refunded.Id);
        Assert.True(refundRow.Refunded);
        Assert.Equal("refunded", refundRow.Status);
        Assert.Equal(0m, refundRow.Amount);
        Assert.NotEqual("active", refundRow.Status);

        var cancelRow = result.Data.StartedRows.Single(r => r.Id == deskCancel.Id);
        Assert.False(cancelRow.Refunded);
        Assert.Equal("cancelled", cancelRow.Status);
        Assert.Equal(150m, cancelRow.Amount);
    }

    private static Guid SeedMember(GymFlowProDbContext ctx, Guid tenantId, string name)
    {
        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = $"M-{Guid.NewGuid():N}"[..12],
            FullName = name,
            FullNameAr = name,
            PhoneNumber = "01000000000",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.GymMembers.Add(member);
        return member.Id;
    }

    private static MembershipPlan SeedPlan(GymFlowProDbContext ctx, Guid tenantId, string name, decimal price)
    {
        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = name,
            NameAr = name,
            PlanType = "monthly_unlimited",
            DurationDays = 30,
            Price = price,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.MembershipPlans.Add(plan);
        return plan;
    }

    private static async Task<Membership> SeedMembershipSaleAsync(
        GymFlowProDbContext ctx, Guid tenantId, Guid memberId, Guid planId, Guid staffId,
        DateOnly start, DateTime paidAtUtc, decimal cashIn, decimal amountPaidField, bool renewal)
    {
        var membership = new Membership
        {
            TenantId = tenantId,
            MemberId = memberId,
            PlanId = planId,
            StartDate = start,
            EndDate = start.AddDays(30),
            Status = "active",
            AmountPaid = amountPaidField,
            PaymentDate = paidAtUtc,
            LastRenewalDate = renewal ? paidAtUtc : null,
            PlanTransitionMode = renewal ? "cancel_and_switch" : null,
            CreatedAtUtc = paidAtUtc
        };
        ctx.Memberships.Add(membership);

        var sale = new Sale
        {
            TenantId = tenantId,
            MemberId = memberId,
            SoldByUserId = staffId,
            Subtotal = amountPaidField,
            Total = amountPaidField,
            Status = "partially_paid",
            AmountDue = Math.Max(0m, amountPaidField - cashIn)
        };
        ctx.Sales.Add(sale);
        ctx.SaleLines.Add(new SaleLine
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            LineType = "membership",
            ReferenceId = membership.Id,
            Description = "Plan",
            Qty = 1,
            UnitPrice = amountPaidField,
            LineTotal = amountPaidField
        });
        ctx.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = tenantId,
            MemberId = memberId,
            MembershipId = membership.Id,
            Gateway = "cash",
            ExternalRef = Guid.NewGuid().ToString(),
            Amount = cashIn,
            Status = "success",
            PaidAtUtc = paidAtUtc,
            SaleId = sale.Id,
            Method = "cash",
            ReceivedByUserId = staffId
        });
        await ctx.SaveChangesAsync();
        return membership;
    }

    [Fact]
    public async Task Products_UsesSaleLineQtyAndTotal_NotCatalogPrice()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        var water = SeedProduct(ctx, tenantId, "Water", sellPrice: 99m);
        var bar = SeedProduct(ctx, tenantId, "Protein Bar", sellPrice: 200m);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(10);

        await SeedRetailLineAsync(ctx, tenantId, staffId, water.Id, "Water", at, qty: 2, lineTotal: 40m, method: "cash");
        await SeedRetailLineAsync(ctx, tenantId, staffId, bar.Id, "Protein Bar", at.AddMinutes(1), qty: 1, lineTotal: 50m, method: "cash");

        var other = Guid.NewGuid();
        await SeedRetailLineAsync(ctx, other, staffId, water.Id, "Water", at, qty: 9, lineTotal: 999m, method: "cash");

        var result = await svc.GetProductsReportAsync(tenantId, day, day);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3, result.Data!.UnitsSold);
        Assert.Equal(90m, result.Data.Revenue);
        Assert.Equal(2, result.Data.TransactionCount);
        Assert.Equal("Water", result.Data.TopProductName);
        Assert.Equal(2, result.Data.Lines.Count);
        Assert.DoesNotContain(result.Data.Lines, r => r.Revenue == 99m);

        var onlyWater = await svc.GetProductsReportAsync(tenantId, day, day, productId: water.Id);
        Assert.Equal(2, onlyWater.Data!.UnitsSold);
        Assert.Equal(40m, onlyWater.Data.Revenue);
        Assert.Equal(1, onlyWater.Data.TransactionCount);

        var byStaff = await svc.GetProductsReportAsync(tenantId, day, day, staffId: staffId);
        Assert.Equal(3, byStaff.Data!.UnitsSold);
    }

    [Fact]
    public async Task Products_FullRefundDropsOut_RequestedRefundStays()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        var towel = SeedProduct(ctx, tenantId, "Towel", sellPrice: 80m);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(12);

        var kept = await SeedRetailLineAsync(ctx, tenantId, staffId, towel.Id, "Towel", at, qty: 1, lineTotal: 50m, method: "cash");
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = kept.SaleId,
            Amount = 50m,
            Method = "cash",
            Reason = "asked",
            RequestedByUserId = staffId,
            Status = "requested"
        });

        var refunded = await SeedRetailLineAsync(ctx, tenantId, staffId, towel.Id, "Towel", at.AddMinutes(2), qty: 3, lineTotal: 150m, method: "cash");
        var refundSale = await ctx.Sales.FirstAsync(s => s.Id == refunded.SaleId);
        refundSale.Status = "refunded";
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = refunded.SaleId,
            Amount = 150m,
            Method = "cash",
            Reason = "returned",
            RequestedByUserId = staffId,
            Status = "executed",
            ExecutedAt = at.AddMinutes(5)
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetProductsReportAsync(tenantId, day, day);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Data!.UnitsSold);
        Assert.Equal(50m, result.Data.Revenue);
        Assert.Equal(1, result.Data.TransactionCount);
        Assert.Single(result.Data.Lines);
    }

    [Fact]
    public async Task StaffShifts_SalesAndRefunds_MatchSalesAndRefundReports()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var a = SeedStaff(ctx, tenantId);
        var b = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(9);

        await SeedPaymentAsync(ctx, tenantId, a, at, 200m, "cash");
        await SeedPaymentAsync(ctx, tenantId, b, at, 80m, "cash");
        var refundPay = await SeedPaymentAsync(ctx, tenantId, a, at.AddMinutes(5), 50m, "cash");
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = refundPay.SaleId!.Value,
            Amount = 50m,
            Method = "cash",
            Reason = "test",
            RequestedByUserId = a,
            Status = "executed",
            ExecutedAt = at.AddMinutes(20)
        });
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = refundPay.SaleId!.Value,
            Amount = 40m,
            Method = "cash",
            Reason = "pending",
            RequestedByUserId = a,
            Status = "requested"
        });
        await ctx.SaveChangesAsync();

        var staff = await svc.GetStaffShiftsReportAsync(tenantId, day, day);
        var sales = await svc.GetSalesReportAsync(tenantId, day, day);
        var refunds = await svc.GetRefundsReportAsync(tenantId, day, day);

        Assert.True(staff.IsSuccess, staff.Error);
        Assert.Equal(sales.Data!.CashInTotal, staff.Data!.Sales);
        Assert.Equal(sales.Data.TransactionCount, staff.Data.TransactionCount);
        Assert.Equal(330m, staff.Data.Sales);
        Assert.Equal(3, staff.Data.TransactionCount);
        Assert.Equal(staff.Data.StaffCashIn.Sum(r => r.CashIn), staff.Data.Sales);
        Assert.Equal(refunds.Data!.Total, staff.Data.Refunds);
        Assert.Equal(50m, staff.Data.Refunds);
        Assert.Empty(staff.Data.Transactions);
    }

    [Fact]
    public async Task StaffShifts_OpenedAtGrain_IgnoresOtherTenant_ShiftSalesAndCashRefunds()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var day = new DateOnly(2026, 8, 18);
        var (utcStart, _) = GMS.Core.Utilities.MembershipOperational.CairoInclusiveRangeUtc(day, day);
        var at = utcStart.AddHours(10);
        var yesterday = utcStart.AddHours(-4);

        var todayShift = SeedShift(ctx, tenantId, staffId, at, "closed", at.AddHours(6));
        var oldShift = SeedShift(ctx, tenantId, staffId, yesterday, "closed", yesterday.AddHours(4));
        var other = Guid.NewGuid();
        SeedShift(ctx, other, staffId, at, "open");
        await ctx.SaveChangesAsync();

        await SeedPaymentAsync(ctx, tenantId, staffId, at, 200m, "cash", shiftId: todayShift.Id);
        await SeedPaymentAsync(ctx, tenantId, staffId, at, 80m, "card_paymob");
        await SeedPaymentAsync(ctx, other, staffId, at, 999m, "cash", shiftId: todayShift.Id);

        var refundPay = await SeedPaymentAsync(ctx, tenantId, staffId, at, 60m, "cash", shiftId: todayShift.Id);
        var cashRefund = new Refund
        {
            TenantId = tenantId,
            SaleId = refundPay.SaleId!.Value,
            Amount = 30m,
            Method = "cash",
            Reason = "cash back",
            RequestedByUserId = staffId,
            Status = "executed",
            ExecutedAt = at.AddMinutes(15)
        };
        ctx.Refunds.Add(cashRefund);
        var creditRefund = new Refund
        {
            TenantId = tenantId,
            SaleId = refundPay.SaleId!.Value,
            Amount = 10m,
            Method = "credit",
            Reason = "credit",
            RequestedByUserId = staffId,
            Status = "executed",
            ExecutedAt = at.AddMinutes(16)
        };
        ctx.Refunds.Add(creditRefund);
        await ctx.SaveChangesAsync();
        ctx.CashMovements.Add(new CashMovement
        {
            TenantId = tenantId,
            ShiftId = todayShift.Id,
            Type = "refund",
            Amount = -30m,
            ReferenceId = cashRefund.Id,
            CreatedByUserId = staffId
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetStaffShiftsReportAsync(tenantId, day, day);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(340m, result.Data!.Sales);
        Assert.Equal(40m, result.Data.Refunds);
        Assert.Equal(1, result.Data.ShiftCount);
        Assert.Single(result.Data.Shifts);
        Assert.Equal(todayShift.Id, result.Data.Shifts[0].ShiftId);
        Assert.Equal(260m, result.Data.Shifts[0].Sales);
        Assert.Equal(30m, result.Data.Shifts[0].Refunds);
        Assert.DoesNotContain(result.Data.Shifts, s => s.ShiftId == oldShift.Id);
        Assert.Equal(1, result.Data.StaffCashIn.Sum(r => r.ShiftCount));

        var byShift = await svc.GetStaffShiftsReportAsync(tenantId, day, day, shiftId: todayShift.Id);
        Assert.Equal(260m, byShift.Data!.Sales);
        Assert.Equal(30m, byShift.Data.Refunds);
        Assert.Equal(1, byShift.Data.ShiftCount);
        Assert.Contains(byShift.Data.Transactions, t => t.Type == "sale");
        Assert.Contains(byShift.Data.Transactions, t => t.Type == "refund");

        var byStaff = await svc.GetStaffShiftsReportAsync(tenantId, day, day, staffId: staffId);
        Assert.Equal(340m, byStaff.Data!.Sales);
        Assert.True(byStaff.Data.Transactions.Count > 0);
    }

    private static Product SeedProduct(GymFlowProDbContext ctx, Guid tenantId, string name, decimal sellPrice)
    {
        var product = new Product
        {
            TenantId = tenantId,
            Sku = $"SKU-{Guid.NewGuid():N}"[..12],
            Name = name,
            UnitOfMeasure = "pcs",
            SellPrice = sellPrice,
            CostPrice = 1m,
            Currency = "EGP",
            IsSellable = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Products.Add(product);
        return product;
    }

    private static async Task<SaleLine> SeedRetailLineAsync(
        GymFlowProDbContext ctx, Guid tenantId, Guid staffId, Guid productId, string name,
        DateTime soldAtUtc, int qty, decimal lineTotal, string method)
    {
        var sale = new Sale
        {
            TenantId = tenantId,
            SoldByUserId = staffId,
            Subtotal = lineTotal,
            Total = lineTotal,
            Status = "completed",
            CreatedAtUtc = soldAtUtc
        };
        ctx.Sales.Add(sale);
        var line = new SaleLine
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            LineType = "retail",
            ReferenceId = productId,
            Description = name,
            Qty = qty,
            UnitPrice = qty == 0 ? 0 : lineTotal / qty,
            LineTotal = lineTotal,
            CreatedAtUtc = soldAtUtc
        };
        ctx.SaleLines.Add(line);
        ctx.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = tenantId,
            Gateway = method,
            ExternalRef = Guid.NewGuid().ToString(),
            Amount = lineTotal,
            Status = "success",
            PaidAtUtc = soldAtUtc,
            SaleId = sale.Id,
            Method = method,
            ReceivedByUserId = staffId
        });
        await ctx.SaveChangesAsync();
        return line;
    }

    private sealed class NoopInventoryReports : IInventoryReportService
    {
        public Task<Result<InventorySummaryReportDto>> GetSummaryAsync(Guid tenantId, bool includeValuation) =>
            Task.FromResult(Result<InventorySummaryReportDto>.Failure("noop"));

        public Task<Result<List<InventoryMovementReportRowDto>>> GetMovementsAsync(
            Guid tenantId, InventoryMovementQueryRequest request) =>
            Task.FromResult(Result<List<InventoryMovementReportRowDto>>.Failure("noop"));

        public Task<Result<List<InventoryReorderSuggestionDto>>> GetReorderSuggestionsAsync(
            Guid tenantId, bool includeCost = false) =>
            Task.FromResult(Result<List<InventoryReorderSuggestionDto>>.Failure("noop"));

        public Task<Result<List<InventoryDeadStockRowDto>>> GetDeadStockAsync(
            Guid tenantId, int daysIdle = 30, bool includeCost = false) =>
            Task.FromResult(Result<List<InventoryDeadStockRowDto>>.Failure("noop"));

        public Task<Result<List<InventoryProductPerformanceRowDto>>> GetProductPerformanceAsync(
            Guid tenantId, DateTime fromUtc, DateTime toUtc, bool includeMargin, int take = 50) =>
            Task.FromResult(Result<List<InventoryProductPerformanceRowDto>>.Success(new List<InventoryProductPerformanceRowDto>()));

        public Task<Result<InventoryAlertJobResultDto>> RunDailyAlertsAsync(Guid tenantId, DateOnly cairoDate) =>
            Task.FromResult(Result<InventoryAlertJobResultDto>.Failure("noop"));
    }
}
