namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class ShiftZReportTests
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
        var audit = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var svc = new ZReportService(ctx, audit, new ZReportPdfRenderer(), NullLogger<ZReportService>.Instance);
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
            FirstName = "Ahmed",
            LastName = "Hassan",
            Email = $"staff-{Guid.NewGuid()}@test.local",
            Role = "Receptionist"
        };
        ctx.AppUsers.Add(staff);
        return staff.Id;
    }

    [Fact]
    public async Task ShiftClosing_UsesShiftLinkedTx_AndFrozenCashWhenClosed()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var opened = new DateTime(2026, 8, 18, 11, 0, 0, DateTimeKind.Utc);
        var shift = new Shift
        {
            TenantId = tenantId,
            UserId = staffId,
            OpenedAt = opened,
            ClosedAt = opened.AddHours(8),
            OpeningFloat = 100m,
            ExpectedCash = 999m,
            CountedCash = 240m,
            Variance = 240m - 999m,
            Status = "closed"
        };
        ctx.Shifts.Add(shift);

        var sale = new Sale
        {
            TenantId = tenantId,
            SoldByUserId = staffId,
            ShiftId = shift.Id,
            Subtotal = 300m,
            DiscountAmount = 20m,
            Total = 280m,
            Status = "completed"
        };
        ctx.Sales.Add(sale);
        ctx.SaleLines.Add(new SaleLine
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            LineType = "retail",
            Description = "Water",
            Qty = 2,
            UnitPrice = 50m,
            LineTotal = 100m
        });
        ctx.SaleLines.Add(new SaleLine
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            LineType = "membership",
            Description = "Plan",
            Qty = 1,
            UnitPrice = 180m,
            LineTotal = 180m
        });
        ctx.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = tenantId,
            Gateway = "cash",
            ExternalRef = Guid.NewGuid().ToString(),
            Amount = 200m,
            Status = "success",
            PaidAtUtc = opened.AddHours(1),
            SaleId = sale.Id,
            ReceivedByUserId = staffId,
            ShiftId = shift.Id,
            Method = "cash"
        });
        ctx.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = tenantId,
            Gateway = "card_paymob",
            ExternalRef = Guid.NewGuid().ToString(),
            Amount = 80m,
            Status = "success",
            PaidAtUtc = opened.AddHours(2),
            SaleId = sale.Id,
            ReceivedByUserId = staffId,
            ShiftId = shift.Id,
            Method = "card_paymob"
        });

        var refund = new Refund
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            Amount = 30m,
            Method = "cash",
            Reason = "back",
            RequestedByUserId = staffId,
            Status = "executed",
            ExecutedAt = opened.AddHours(3)
        };
        ctx.Refunds.Add(refund);
        ctx.CashMovements.Add(new CashMovement
        {
            TenantId = tenantId,
            ShiftId = shift.Id,
            Type = "sale",
            Amount = 200m,
            ReferenceId = sale.Id,
            CreatedByUserId = staffId
        });
        ctx.CashMovements.Add(new CashMovement
        {
            TenantId = tenantId,
            ShiftId = shift.Id,
            Type = "refund",
            Amount = -30m,
            ReferenceId = refund.Id,
            CreatedByUserId = staffId
        });
        ctx.CashMovements.Add(new CashMovement
        {
            TenantId = tenantId,
            ShiftId = shift.Id,
            Type = "paid_out",
            Amount = -20m,
            Reason = "supplies",
            CreatedByUserId = staffId
        });
        await ctx.SaveChangesAsync();

        var other = Guid.NewGuid();
        var otherShift = new Shift
        {
            TenantId = other,
            UserId = staffId,
            OpenedAt = opened,
            OpeningFloat = 0,
            Status = "closed",
            ExpectedCash = 50m,
            CountedCash = 50m,
            Variance = 0
        };
        ctx.Shifts.Add(otherShift);
        ctx.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = other,
            Gateway = "cash",
            ExternalRef = Guid.NewGuid().ToString(),
            Amount = 5000m,
            Status = "success",
            PaidAtUtc = opened,
            ShiftId = otherShift.Id,
            Method = "cash"
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetShiftClosingAsync(tenantId, shift.Id);
        Assert.True(result.IsSuccess, result.Error);
        var dto = result.Data!;
        Assert.True(dto.IsFinal);
        Assert.True(dto.RevealCash);
        Assert.Equal(300m, dto.GrossSales);
        Assert.Equal(20m, dto.Discounts);
        Assert.Equal(30m, dto.Refunds);
        Assert.Equal(250m, dto.NetSales);
        Assert.Equal(2, dto.TransactionCount);
        Assert.Equal(280m, dto.Methods.Sum(m => m.Total));
        Assert.Equal(200m, dto.CashSales);
        Assert.Equal(30m, dto.CashRefunds);
        Assert.Equal(20m, dto.CashExpenses);
        Assert.Equal(100m, dto.OpeningCash);
        Assert.Equal(999m, dto.ExpectedCash);
        Assert.Equal(240m, dto.CountedCash);
        Assert.Equal(240m - 999m, dto.Difference);
        Assert.Equal(100m, dto.Products);
        Assert.Equal(180m, dto.Memberships);
        Assert.Equal("Ahmed Hassan", dto.StaffName);

        var missing = await svc.GetShiftClosingAsync(tenantId, otherShift.Id);
        Assert.False(missing.IsSuccess);

        var cairoDay = new DateOnly(2026, 8, 18);
        var list = await svc.ListShiftClosingsAsync(tenantId, cairoDay, cairoDay);
        Assert.True(list.IsSuccess, list.Error);
        Assert.Single(list.Data!.Items);
        Assert.Equal(shift.Id, list.Data.Items[0].ShiftId);
        Assert.Equal(280m, list.Data.Items[0].Sales);
        Assert.Equal(30m, list.Data.Items[0].Refunds);

        var pdf = await svc.GetShiftClosingPdfAsync(tenantId, shift.Id);
        Assert.True(pdf.IsSuccess, pdf.Error);
        Assert.True(pdf.Data!.Length > 100);
    }

    [Fact]
    public async Task ShiftClosing_Open_HidesExpectedAndCounted()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var shift = new Shift
        {
            TenantId = tenantId,
            UserId = staffId,
            OpenedAt = DateTime.UtcNow.AddHours(-2),
            OpeningFloat = 50m,
            Status = "open"
        };
        ctx.Shifts.Add(shift);
        ctx.CashMovements.Add(new CashMovement
        {
            TenantId = tenantId,
            ShiftId = shift.Id,
            Type = "sale",
            Amount = 80m,
            CreatedByUserId = staffId
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetShiftClosingAsync(tenantId, shift.Id);
        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Data!.IsFinal);
        Assert.False(result.Data.RevealCash);
        Assert.Null(result.Data.ExpectedCash);
        Assert.Null(result.Data.CountedCash);
        Assert.Null(result.Data.Difference);
        Assert.Equal(50m, result.Data.OpeningCash);
        Assert.Equal(80m, result.Data.CashSales);
    }
}
