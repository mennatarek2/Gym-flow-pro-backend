namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class InventoryReportServiceTests
{
    private sealed class NoOpPush : IPushNotificationService
    {
        public Task SendToDeviceAsync(string token, string title, string body) => Task.CompletedTask;
        public Task SendToTopicAsync(string topic, string title, string body) => Task.CompletedTask;
    }

    private static async Task<(
        GymFlowProDbContext ctx,
        InventoryReportService svc,
        IStockLedgerService ledger,
        Guid tenantId,
        Guid productId,
        Guid warehouseId,
        Guid ownerAppUserId)> SeedAsync(
        decimal onHand = 8,
        decimal reorderMin = 10,
        decimal costPrice = 50)
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

        var owner = new AppUser
        {
            TenantId = tenantId,
            UserId = Guid.NewGuid().ToString(),
            FirstName = "Owner",
            LastName = "One",
            Email = "owner@test.local",
            Role = "Owner",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(owner);

        var product = new Product
        {
            TenantId = tenantId,
            Sku = "PROT-1",
            Name = "Protein",
            UnitOfMeasure = "pcs",
            SellPrice = 100,
            CostPrice = costPrice,
            Currency = "EGP",
            TrackStock = true,
            ReorderMinQty = reorderMin,
            IsActive = true,
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
        if (onHand != 0)
        {
            Assert.True((await ledger.PostAsync(new StockLedgerPostRequest
            {
                TenantId = tenantId,
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                QtyDelta = onHand,
                UnitCost = costPrice,
                Reason = StockMovementReasons.Opening,
                ReferenceType = StockReferenceTypes.StockAdjustment,
                ReferenceId = Guid.NewGuid()
            })).IsSuccess);
        }

        var notifications = new NotificationService(
            ctx,
            new NoOpPush(),
            NullLogger<NotificationService>.Instance,
            new DefaultPermissionProvider(),
            new NullStaffNotificationRealtimeNotifier());
        var reorder = new InventoryReorderCalculator(ctx);
        var svc = new InventoryReportService(
            ctx, notifications, reorder, NullLogger<InventoryReportService>.Instance);

        return (ctx, svc, ledger, tenantId, product.Id, warehouse.Id, owner.Id);
    }

    [Fact]
    public async Task Summary_Valuation_Is_Qty_Times_Cost()
    {
        var (ctx, svc, _, tenantId, _, _, _) = await SeedAsync(onHand: 8, costPrice: 50);

        var withVal = await svc.GetSummaryAsync(tenantId, includeValuation: true);
        Assert.True(withVal.IsSuccess);
        Assert.True(withVal.Data!.IncludesValuation);
        Assert.Equal(400m, withVal.Data.InventoryValueEgp);
        Assert.Equal(1, withVal.Data.LowStockCount);
        Assert.Equal(0, withVal.Data.OutOfStockCount);

        var noVal = await svc.GetSummaryAsync(tenantId, includeValuation: false);
        Assert.True(noVal.IsSuccess);
        Assert.False(noVal.Data!.IncludesValuation);
        Assert.Null(noVal.Data.InventoryValueEgp);

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Summary_ExpiredOnlyStock_IsOutOfStock_AndZeroSellableValue()
    {
        var (ctx, svc, ledger, tenantId, productId, warehouseId, _) =
            await SeedAsync(onHand: 0, reorderMin: 5, costPrice: 40);
        var product = await ctx.Products.SingleAsync(p => p.Id == productId);
        product.TrackBatch = true;
        product.TrackExpiry = true;
        var batch = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = "EXP",
            ExpiresOn = MembershipOperational.TodayCairo().AddDays(-3),
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.ProductBatches.Add(batch);
        await ctx.SaveChangesAsync();
        Assert.True((await ledger.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            BatchId = batch.Id,
            QtyDelta = 12,
            UnitCost = 40,
            Reason = StockMovementReasons.Opening,
            ReferenceType = "TestSeed",
            ReferenceId = Guid.NewGuid()
        })).IsSuccess);

        var summary = await svc.GetSummaryAsync(tenantId, includeValuation: true);
        Assert.True(summary.IsSuccess);
        Assert.Equal(1, summary.Data!.OutOfStockCount);
        Assert.Equal(1, summary.Data.LowStockCount);
        Assert.Equal(0m, summary.Data.InventoryValueEgp);

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Movements_Filter_By_Reason_And_Rejects_Wide_Range()
    {
        var (ctx, svc, ledger, tenantId, productId, warehouseId, _) = await SeedAsync(onHand: 20, reorderMin: 0);

        Assert.True((await ledger.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            QtyDelta = -2,
            Reason = StockMovementReasons.Sale,
            ReferenceType = StockReferenceTypes.SaleLine,
            ReferenceId = Guid.NewGuid()
        })).IsSuccess);

        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(1);

        var sales = await svc.GetMovementsAsync(tenantId, new InventoryMovementQueryRequest
        {
            FromUtc = from,
            ToUtc = to,
            Reason = StockMovementReasons.Sale
        });
        Assert.True(sales.IsSuccess);
        Assert.Single(sales.Data!);
        Assert.Equal(StockMovementReasons.Sale, sales.Data![0].Reason);

        var wide = await svc.GetMovementsAsync(tenantId, new InventoryMovementQueryRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-400),
            ToUtc = DateTime.UtcNow
        });
        Assert.False(wide.IsSuccess);

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task LowStock_Notifies_Once_Per_Day()
    {
        var (ctx, svc, _, tenantId, productId, _, ownerId) = await SeedAsync(onHand: 8, reorderMin: 10);
        var cairoDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time")));

        var first = await svc.RunDailyAlertsAsync(tenantId, cairoDate);
        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Data!.LowStockNotified);
        Assert.Equal(0, first.Data.SkippedDedupe);

        var second = await svc.RunDailyAlertsAsync(tenantId, cairoDate);
        Assert.True(second.IsSuccess);
        Assert.Equal(0, second.Data!.LowStockNotified);
        Assert.Equal(1, second.Data.SkippedDedupe);

        var dedupe = $"inv-low:{cairoDate:yyyyMMdd}:{productId:N}";
        var count = await ctx.Notifications.CountAsync(n =>
            n.TenantId == tenantId && n.AppUserId == ownerId && n.ExternalMessageId == dedupe);
        Assert.Equal(1, count);

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task ReorderSuggestions_OmitCost_WhenIncludeCostFalse()
    {
        var (ctx, svc, _, tenantId, _, _, _) = await SeedAsync(onHand: 2, reorderMin: 10, costPrice: 77);

        var open = await svc.GetReorderSuggestionsAsync(tenantId, includeCost: false);
        Assert.True(open.IsSuccess, open.Error);
        Assert.NotEmpty(open.Data!);
        Assert.All(open.Data!, row => Assert.Null(row.CostPrice));

        var managed = await svc.GetReorderSuggestionsAsync(tenantId, includeCost: true);
        Assert.True(managed.IsSuccess, managed.Error);
        Assert.All(managed.Data!, row => Assert.Equal(77m, row.CostPrice));

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task ReorderSuggestions_Surfaces_Server_Need_Fields()
    {
        var (ctx, svc, _, tenantId, _, _, _) = await SeedAsync(onHand: 2, reorderMin: 10, costPrice: 50);

        var r = await svc.GetReorderSuggestionsAsync(tenantId, includeCost: false);
        Assert.True(r.IsSuccess, r.Error);
        var row = Assert.Single(r.Data!);
        Assert.Equal(2m, row.Available);
        Assert.Equal(8m, row.SuggestedQty);
        Assert.Equal(0m, row.IncomingOpenQty);
        Assert.Equal(0m, row.AvgDailySales);

        await ctx.DisposeAsync();
    }
}
