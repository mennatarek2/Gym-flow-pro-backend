namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public class SupplierServiceAp1Tests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static async Task<(GymFlowProDbContext ctx, SupplierService svc, Guid tenantId, Guid supplierId)> SeedAsync()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new GMS.Infrastructure.Services.TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة",
            GymCode = $"T-{tenantId:N}"[..12],
            City = "Cairo",
            CreatedAtUtc = DateTime.UtcNow
        });
        var supplierId = Guid.NewGuid();
        ctx.Suppliers.Add(new Supplier
        {
            Id = supplierId,
            TenantId = tenantId,
            Name = "Cairo Nutrition",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var svc = new SupplierService(ctx, new NoOpAudit());
        return (ctx, svc, tenantId, supplierId);
    }

    [Fact]
    public async Task Opening_then_payment_reduces_due()
    {
        var (ctx, svc, tenantId, supplierId) = await SeedAsync();

        var open = await svc.PostOpeningAsync(tenantId, supplierId, new PostSupplierOpeningRequest
        {
            Amount = 1000,
            OwedToSupplier = true
        });
        Assert.True(open.IsSuccess);
        Assert.Equal(1000m, open.Data!.Amount);

        var bal1 = await svc.GetBalanceAsync(tenantId, supplierId);
        Assert.True(bal1.IsSuccess);
        Assert.Equal(1000m, bal1.Data!.DueTotal);
        Assert.Equal(1000m, bal1.Data.OpeningTotal);

        var pay = await svc.PostPaymentAsync(tenantId, supplierId, new PostSupplierPaymentRequest
        {
            Amount = 300,
            Method = "cash"
        });
        Assert.True(pay.IsSuccess);
        Assert.Equal(-300m, pay.Data!.Amount);
        Assert.Equal(SupplierLedgerReasons.Payment, pay.Data.Reason);

        var bal2 = await svc.GetBalanceAsync(tenantId, supplierId);
        Assert.Equal(700m, bal2.Data!.DueTotal);
        Assert.Equal(300m, bal2.Data.PaidTotal);

        // Payment never posts stock movements
        Assert.False(await ctx.StockMovements.AnyAsync());
    }

    [Fact]
    public async Task Second_opening_rejected()
    {
        var (_, svc, tenantId, supplierId) = await SeedAsync();
        var first = await svc.PostOpeningAsync(tenantId, supplierId, new PostSupplierOpeningRequest
        {
            Amount = 50,
            OwedToSupplier = true
        });
        Assert.True(first.IsSuccess);
        var second = await svc.PostOpeningAsync(tenantId, supplierId, new PostSupplierOpeningRequest
        {
            Amount = 10,
            OwedToSupplier = false
        });
        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task Create_with_opening_and_address()
    {
        var (ctx, svc, tenantId, _) = await SeedAsync();
        var created = await svc.CreateAsync(tenantId, new CreateSupplierRequest
        {
            Name = "Delta",
            Address = "Nasr City",
            OpeningAmount = 200,
            OpeningOwedToSupplier = true
        });
        Assert.True(created.IsSuccess);
        Assert.Equal("Nasr City", created.Data!.Address);
        Assert.Equal(200m, created.Data.DueTotal);

        var openingRows = await ctx.SupplierLedgerEntries
            .Where(e => e.SupplierId == created.Data.Id && e.Reason == SupplierLedgerReasons.Opening)
            .ToListAsync();
        Assert.Single(openingRows);
    }

    [Fact]
    public async Task List_without_money_omits_totals()
    {
        var (_, svc, tenantId, supplierId) = await SeedAsync();
        await svc.PostOpeningAsync(tenantId, supplierId, new PostSupplierOpeningRequest
        {
            Amount = 10,
            OwedToSupplier = true
        });
        var list = await svc.ListAsync(tenantId, includeMoney: false);
        Assert.True(list.IsSuccess);
        var row = list.Data!.Single(s => s.Id == supplierId);
        Assert.Null(row.DueTotal);
        Assert.Null(row.PurchasesTotal);
        Assert.Null(row.PaidTotal);

        var withMoney = await svc.ListAsync(tenantId, includeMoney: true);
        var row2 = withMoney.Data!.Single(s => s.Id == supplierId);
        Assert.Equal(10m, row2.DueTotal);
    }

    [Fact]
    public async Task Purchase_ledger_counts_in_purchases_and_due()
    {
        var (ctx, svc, tenantId, supplierId) = await SeedAsync();
        ctx.SupplierLedgerEntries.Add(new SupplierLedgerEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SupplierId = supplierId,
            Amount = 500,
            Reason = SupplierLedgerReasons.Purchase,
            ReferenceType = "GoodsReceipt",
            ReferenceId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var bal = await svc.GetBalanceAsync(tenantId, supplierId);
        Assert.Equal(500m, bal.Data!.PurchasesTotal);
        Assert.Equal(500m, bal.Data.DueTotal);
        Assert.Equal(0m, bal.Data.PaidTotal);
    }
}
