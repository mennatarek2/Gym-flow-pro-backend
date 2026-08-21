namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class ZReportServiceTests
{
    private static (GymFlowProDbContext ctx, ZReportService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var pdfRenderer = new ZReportPdfRenderer();
        var svc = new ZReportService(ctx, auditService, pdfRenderer, NullLogger<ZReportService>.Instance);

        return (ctx, svc, tenantId);
    }

    private static void SeedTenant(GymFlowProDbContext ctx, Guid tenantId)
    {
        ctx.Tenants.Add(new Tenant
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

    /// <summary>
    /// Seeds a Sale + SaleLine + PaymentTransaction and backdates CreatedAtUtc to
    /// <paramref name="createdAtUtc"/>. BaseEntity.CreatedAtUtc is forcibly reset to
    /// DateTime.UtcNow on the first SaveChangesAsync (see GymFlowProDbContext.SaveChangesAsync), so
    /// this does a second save (State=Modified only touches UpdatedAtUtc) to make the backdate stick.
    /// </summary>
    private static async Task<Sale> SeedSaleAsync(
        GymFlowProDbContext ctx, Guid tenantId, Guid soldByUserId, DateTime createdAtUtc,
        string lineType, decimal total, decimal discountAmount, decimal? manualDiscountAmount,
        decimal amountDue, string status, string paymentMethod)
    {
        var sale = new Sale
        {
            TenantId = tenantId,
            SoldByUserId = soldByUserId,
            Subtotal = total,
            DiscountAmount = discountAmount,
            ManualDiscountAmount = manualDiscountAmount,
            TaxAmount = 0,
            Total = total,
            AmountDue = amountDue,
            Status = status
        };
        ctx.Sales.Add(sale);

        var line = new SaleLine
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            LineType = lineType,
            Description = lineType,
            Qty = 1,
            UnitPrice = total,
            LineTotal = total
        };
        ctx.SaleLines.Add(line);

        var payment = new PaymentTransaction
        {
            TenantId = tenantId,
            MemberId = Guid.NewGuid(),
            MembershipId = Guid.NewGuid(),
            Gateway = paymentMethod,
            ExternalRef = Guid.NewGuid().ToString(),
            Amount = total - amountDue,
            Status = "success",
            PaidAtUtc = createdAtUtc,
            SaleId = sale.Id,
            Method = paymentMethod
        };
        ctx.PaymentTransactions.Add(payment);

        await ctx.SaveChangesAsync();

        sale.CreatedAtUtc = createdAtUtc;
        await ctx.SaveChangesAsync();

        return sale;
    }

    [Fact]
    public async Task BuildAsync_MixedSalesFixture_AggregatesExactTotals()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var reportDate = new DateOnly(2026, 1, 15);
        var withinDay = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        // A: cash, membership, no discount
        var saleA = await SeedSaleAsync(ctx, tenantId, staffId, withinDay,
            "membership", total: 500m, discountAmount: 0m, manualDiscountAmount: null,
            amountDue: 0m, status: "completed", paymentMethod: "cash");

        // A refund executed the same business day must be picked up by the Z-Report.
        ctx.Refunds.Add(new Refund
        {
            TenantId = tenantId,
            SaleId = saleA.Id,
            Amount = 60m,
            Method = "cash",
            Reason = "Test refund",
            RequestedByUserId = staffId,
            ApprovedByUserId = staffId,
            Status = "executed",
            ExecutedAt = withinDay.AddHours(1)
        });
        await ctx.SaveChangesAsync();

        // B: card_paymob, retail, promo discount 30
        await SeedSaleAsync(ctx, tenantId, staffId, withinDay.AddHours(2),
            "retail", total: 300m, discountAmount: 30m, manualDiscountAmount: null,
            amountDue: 0m, status: "completed", paymentMethod: "card_paymob");

        // C: cash, day_pass, partial payment (150 paid, 50 outstanding)
        await SeedSaleAsync(ctx, tenantId, staffId, withinDay.AddHours(4),
            "day_pass", total: 200m, discountAmount: 0m, manualDiscountAmount: null,
            amountDue: 50m, status: "partially_paid", paymentMethod: "cash");

        // D: cash, membership, manual discount override 40
        await SeedSaleAsync(ctx, tenantId, staffId, withinDay.AddHours(6),
            "membership", total: 400m, discountAmount: 0m, manualDiscountAmount: 40m,
            amountDue: 0m, status: "completed", paymentMethod: "cash");

        var result = await svc.BuildAsync(tenantId, reportDate);

        Assert.True(result.IsSuccess, result.Error);
        var dto = result.Data!;

        var cash = dto.MethodTotals.Single(m => m.Method == "cash");
        Assert.Equal(3, cash.Count); // A, C, D
        Assert.Equal(500m + 150m + 400m, cash.Total);

        var card = dto.MethodTotals.Single(m => m.Method == "card_paymob");
        Assert.Equal(1, card.Count);
        Assert.Equal(300m, card.Total);

        var membership = dto.LineTypeTotals.Single(l => l.LineType == "membership");
        Assert.Equal(2, membership.Count);
        Assert.Equal(900m, membership.Revenue);

        var retail = dto.LineTypeTotals.Single(l => l.LineType == "retail");
        Assert.Equal(1, retail.Count);
        Assert.Equal(300m, retail.Revenue);

        var dayPass = dto.LineTypeTotals.Single(l => l.LineType == "day_pass");
        Assert.Equal(1, dayPass.Count);
        Assert.Equal(200m, dayPass.Revenue);

        Assert.Equal(30m, dto.PromoDiscountTotal);
        Assert.Equal(40m, dto.ManualDiscountTotal);
        Assert.Equal(1, dto.ManualDiscountCount);
        Assert.Equal(60m, dto.RefundsTotal);
        Assert.Equal(50m, dto.OutstandingAddedToday);
        Assert.Equal(900m, dto.MembershipRevenueToday);
    }

    [Fact]
    public async Task BuildAsync_CalledTwiceForSameDate_ReturnsIdenticalPayloadWithoutRecomputing()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var reportDate = new DateOnly(2026, 2, 1);
        var withinDay = new DateTime(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc);

        await SeedSaleAsync(ctx, tenantId, staffId, withinDay,
            "membership", total: 100m, discountAmount: 0m, manualDiscountAmount: null,
            amountDue: 0m, status: "completed", paymentMethod: "cash");

        var first = await svc.BuildAsync(tenantId, reportDate);
        Assert.True(first.IsSuccess, first.Error);

        // Add more data AFTER the first build — since the report is immutable, this must NOT
        // be picked up by a second BuildAsync call for the same date.
        await SeedSaleAsync(ctx, tenantId, staffId, withinDay.AddHours(1),
            "retail", total: 999m, discountAmount: 0m, manualDiscountAmount: null,
            amountDue: 0m, status: "completed", paymentMethod: "fawry");

        var second = await svc.BuildAsync(tenantId, reportDate);
        Assert.True(second.IsSuccess, second.Error);

        Assert.Equal(first.Data!.Id, second.Data!.Id);
        Assert.Single(second.Data.LineTypeTotals); // still just the original membership line — no recompute
        Assert.Equal(100m, second.Data.LineTypeTotals.Single().Revenue);

        var reportCount = await ctx.ZReports.CountAsync(z => z.TenantId == tenantId && z.ReportDate == reportDate);
        Assert.Equal(1, reportCount);
    }

    [Fact]
    public async Task BuildAsync_CairoBusinessDayBoundary_SplitsSalesAcrossCalendarDaysCorrectly()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        // 21:30 UTC on Jan 15 = 23:30 Cairo (UTC+2), same calendar day -> belongs to the Jan 15 report.
        await SeedSaleAsync(ctx, tenantId, staffId,
            new DateTime(2026, 1, 15, 21, 30, 0, DateTimeKind.Utc),
            "membership", total: 111m, discountAmount: 0m, manualDiscountAmount: null,
            amountDue: 0m, status: "completed", paymentMethod: "cash");

        // 22:01 UTC on Jan 15 = 00:01 Cairo on Jan 16 -> belongs to the Jan 16 report.
        await SeedSaleAsync(ctx, tenantId, staffId,
            new DateTime(2026, 1, 15, 22, 1, 0, DateTimeKind.Utc),
            "membership", total: 222m, discountAmount: 0m, manualDiscountAmount: null,
            amountDue: 0m, status: "completed", paymentMethod: "cash");

        var jan15 = await svc.BuildAsync(tenantId, new DateOnly(2026, 1, 15));
        var jan16 = await svc.BuildAsync(tenantId, new DateOnly(2026, 1, 16));

        Assert.True(jan15.IsSuccess, jan15.Error);
        Assert.True(jan16.IsSuccess, jan16.Error);

        var jan15Line = jan15.Data!.LineTypeTotals.Single();
        Assert.Equal(1, jan15Line.Count);
        Assert.Equal(111m, jan15Line.Revenue);

        var jan16Line = jan16.Data!.LineTypeTotals.Single();
        Assert.Equal(1, jan16Line.Count);
        Assert.Equal(222m, jan16Line.Revenue);
    }

    [Fact]
    public async Task RegenerateAsync_RecomputesPayloadAndAuditsTheAction()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        var managerId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var reportDate = new DateOnly(2026, 3, 1);
        var withinDay = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

        await SeedSaleAsync(ctx, tenantId, staffId, withinDay,
            "membership", total: 100m, discountAmount: 0m, manualDiscountAmount: null,
            amountDue: 0m, status: "completed", paymentMethod: "cash");

        var built = await svc.BuildAsync(tenantId, reportDate);
        Assert.True(built.IsSuccess, built.Error);
        Assert.Single(built.Data!.LineTypeTotals);

        // Late correction: another sale lands for the same business day after the initial build.
        await SeedSaleAsync(ctx, tenantId, staffId, withinDay.AddHours(3),
            "retail", total: 50m, discountAmount: 0m, manualDiscountAmount: null,
            amountDue: 0m, status: "completed", paymentMethod: "fawry");

        var regenerated = await svc.RegenerateAsync(tenantId, reportDate, managerId);

        Assert.True(regenerated.IsSuccess, regenerated.Error);
        Assert.Equal(2, regenerated.Data!.LineTypeTotals.Count);
        Assert.Equal(managerId, regenerated.Data.GeneratedByUserId);

        var reportEntity = await ctx.ZReports.FirstAsync(z => z.TenantId == tenantId && z.ReportDate == reportDate);
        Assert.NotEqual(built.Data.GeneratedAt, reportEntity.GeneratedAt);

        var auditEvent = await ctx.AuditEvents
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Action == "zreport.regenerate");
        Assert.NotNull(auditEvent);
        Assert.Equal(reportEntity.Id, auditEvent!.EntityId);
    }
}
