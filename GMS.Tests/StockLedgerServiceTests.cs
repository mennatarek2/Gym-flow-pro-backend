namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class StockLedgerServiceTests
{
    private static async Task<(GymFlowProDbContext ctx, StockLedgerService svc, Guid tenantId, Guid productId, Guid warehouseId)> SeedAsync(
        bool trackStock = true, bool allowFractional = false)
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

        var product = new Product
        {
            TenantId = tenantId,
            Sku = "PROT-1",
            Name = "Protein",
            UnitOfMeasure = "pcs",
            SellPrice = 100,
            CostPrice = 50,
            Currency = "EGP",
            TrackStock = trackStock,
            AllowFractionalQty = allowFractional,
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

        var svc = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);
        return (ctx, svc, tenantId, product.Id, warehouse.Id);
    }

    [Fact]
    public async Task Post_Plus100_ThenMinus5_Yields95()
    {
        var (_, svc, tenantId, productId, warehouseId) = await SeedAsync();

        var open = await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            QtyDelta = 100,
            Reason = StockMovementReasons.Opening,
            UnitCost = 50
        });
        Assert.True(open.IsSuccess, open.Error);

        var adj = await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            QtyDelta = -5,
            Reason = StockMovementReasons.Adjustment
        });
        Assert.True(adj.IsSuccess, adj.Error);

        var onHand = await svc.GetOnHandAsync(tenantId, productId, warehouseId);
        Assert.True(onHand.IsSuccess);
        Assert.Equal(95m, onHand.Data);
    }

    [Fact]
    public async Task DuplicateSaleReference_DoesNotDoublePost()
    {
        var (ctx, svc, tenantId, productId, warehouseId) = await SeedAsync();
        var saleLineId = Guid.NewGuid();

        await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            QtyDelta = 10,
            Reason = StockMovementReasons.Opening
        });

        var first = await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            QtyDelta = -2,
            Reason = StockMovementReasons.Sale,
            ReferenceType = StockReferenceTypes.SaleLine,
            ReferenceId = saleLineId
        });
        Assert.True(first.IsSuccess, first.Error);

        var second = await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            QtyDelta = -2,
            Reason = StockMovementReasons.Sale,
            ReferenceType = StockReferenceTypes.SaleLine,
            ReferenceId = saleLineId
        });
        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(first.Data!.Id, second.Data!.Id);

        Assert.Equal(1, await ctx.StockMovements.CountAsync(m => m.Reason == StockMovementReasons.Sale));
        var onHand = await svc.GetOnHandAsync(tenantId, productId, warehouseId);
        Assert.Equal(8m, onHand.Data);
    }

    [Fact]
    public async Task CrossTenant_ProductRejected()
    {
        var (_, svc, tenantId, _, warehouseId) = await SeedAsync();
        var otherProduct = Guid.NewGuid();

        var result = await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = otherProduct,
            WarehouseId = warehouseId,
            QtyDelta = 1,
            Reason = StockMovementReasons.Opening
        });
        Assert.False(result.IsSuccess);
        Assert.Contains("Product", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InsufficientStock_Rejected()
    {
        var (_, svc, tenantId, productId, warehouseId) = await SeedAsync();

        var result = await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            QtyDelta = -1,
            Reason = StockMovementReasons.Sale,
            ReferenceType = StockReferenceTypes.SaleLine,
            ReferenceId = Guid.NewGuid()
        });
        Assert.False(result.IsSuccess);
        Assert.Contains("Insufficient", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductBreakdown_SumsWarehouses()
    {
        var (ctx, svc, tenantId, productId, warehouseId) = await SeedAsync();
        var desk = new Warehouse
        {
            TenantId = tenantId,
            Code = "DESK",
            Name = "Desk",
            IsDefault = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Warehouses.Add(desk);
        await ctx.SaveChangesAsync();

        await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId, ProductId = productId, WarehouseId = warehouseId,
            QtyDelta = 40, Reason = StockMovementReasons.Opening
        });
        await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId, ProductId = productId, WarehouseId = desk.Id,
            QtyDelta = 10, Reason = StockMovementReasons.Opening
        });

        var breakdown = await svc.GetProductStockBreakdownAsync(tenantId, productId);
        Assert.True(breakdown.IsSuccess, breakdown.Error);
        Assert.Equal(50m, breakdown.Data!.TotalOnHand);
        Assert.Equal(50m, breakdown.Data.TotalAvailable);
        Assert.Equal(2, breakdown.Data.Warehouses.Count);
    }

    [Fact]
    public async Task Available_ExcludesExpired_Batches()
    {
        var (ctx, svc, tenantId, productId, warehouseId) = await SeedAsync();
        var product = await ctx.Products.SingleAsync(p => p.Id == productId);
        product.TrackBatch = true;
        product.TrackExpiry = true;
        await ctx.SaveChangesAsync();

        var today = GMS.Core.Utilities.MembershipOperational.TodayCairo();
        var expired = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = "EXP",
            ExpiresOn = today.AddDays(-1),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30)
        };
        var good = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = "GOOD",
            ExpiresOn = today.AddDays(30),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        };
        ctx.ProductBatches.AddRange(expired, good);
        await ctx.SaveChangesAsync();

        await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId, ProductId = productId, WarehouseId = warehouseId,
            BatchId = expired.Id, QtyDelta = 5, Reason = StockMovementReasons.PurchaseReceipt,
            ReferenceType = "GRN", ReferenceId = Guid.NewGuid()
        });
        await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId, ProductId = productId, WarehouseId = warehouseId,
            BatchId = good.Id, QtyDelta = 3, Reason = StockMovementReasons.PurchaseReceipt,
            ReferenceType = "GRN", ReferenceId = Guid.NewGuid()
        });

        var physical = await svc.GetOnHandAsync(tenantId, productId, warehouseId);
        var available = await svc.GetAvailableAsync(tenantId, productId, warehouseId);
        Assert.Equal(8m, physical.Data);
        Assert.Equal(3m, available.Data);

        var board = await svc.GetStockBoardAsync(tenantId, warehouseId);
        var row = board.Data!.Single(r => r.ProductId == productId);
        Assert.Equal(8m, row.OnHand);
        Assert.Equal(3m, row.Available);
    }

    [Fact]
    public async Task AllocateSale_Fefo_EarliestExpiryFirst()
    {
        var (ctx, svc, tenantId, productId, warehouseId) = await SeedAsync();
        var product = await ctx.Products.SingleAsync(p => p.Id == productId);
        product.TrackBatch = true;
        product.TrackExpiry = true;
        await ctx.SaveChangesAsync();

        var today = GMS.Core.Utilities.MembershipOperational.TodayCairo();
        var later = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = "LATER",
            ExpiresOn = today.AddDays(60),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-20)
        };
        var sooner = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = "SOON",
            ExpiresOn = today.AddDays(10),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
        };
        ctx.ProductBatches.AddRange(later, sooner);
        await ctx.SaveChangesAsync();

        await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId, ProductId = productId, WarehouseId = warehouseId,
            BatchId = later.Id, QtyDelta = 5, Reason = StockMovementReasons.PurchaseReceipt,
            ReferenceType = "GRN", ReferenceId = Guid.NewGuid()
        });
        await svc.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId, ProductId = productId, WarehouseId = warehouseId,
            BatchId = sooner.Id, QtyDelta = 5, Reason = StockMovementReasons.PurchaseReceipt,
            ReferenceType = "GRN", ReferenceId = Guid.NewGuid()
        });

        var alloc = await svc.AllocateSaleAsync(tenantId, productId, warehouseId, 7m);
        Assert.True(alloc.IsSuccess, alloc.Error);
        Assert.Equal(2, alloc.Data!.Count);
        Assert.Equal(sooner.Id, alloc.Data[0].BatchId);
        Assert.Equal(5m, alloc.Data[0].Qty);
        Assert.Equal(later.Id, alloc.Data[1].BatchId);
        Assert.Equal(2m, alloc.Data[1].Qty);
    }
}
