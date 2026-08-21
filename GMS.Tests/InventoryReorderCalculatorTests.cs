namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class InventoryReorderCalculatorTests
{
    private static async Task<(
        GymFlowProDbContext ctx,
        InventoryReorderCalculator calc,
        Guid tenantId,
        Guid productId,
        Guid warehouseId,
        Guid supplierId,
        Guid ownerId)> SeedProductAsync(
        decimal reorderMin = 10,
        bool trackExpiry = false)
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
            CostPrice = 40,
            Currency = "EGP",
            TrackStock = true,
            TrackExpiry = trackExpiry,
            IsPurchasable = true,
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
        var supplier = new Supplier
        {
            TenantId = tenantId,
            Name = "NutraCo",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Products.Add(product);
        ctx.Warehouses.Add(warehouse);
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();

        var calc = new InventoryReorderCalculator(ctx);
        return (ctx, calc, tenantId, product.Id, warehouse.Id, supplier.Id, owner.Id);
    }

    private static async Task PostOpeningAsync(
        GymFlowProDbContext ctx, Guid tenantId, Guid productId, Guid warehouseId, decimal qty, Guid? batchId = null)
    {
        var ledger = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);
        var r = await ledger.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            BatchId = batchId,
            QtyDelta = qty,
            UnitCost = 40,
            Reason = StockMovementReasons.Opening,
            ReferenceType = StockReferenceTypes.StockAdjustment,
            ReferenceId = Guid.NewGuid()
        });
        Assert.True(r.IsSuccess, r.Error);
    }

    [Fact]
    public async Task MinFloor_Suggests_Gap_Using_Available()
    {
        var (ctx, calc, tenantId, productId, warehouseId, _, _) = await SeedProductAsync(reorderMin: 10);
        await PostOpeningAsync(ctx, tenantId, productId, warehouseId, 2);

        var r = await calc.CalculateAsync(tenantId, includeCost: true);
        Assert.True(r.IsSuccess, r.Error);
        var row = Assert.Single(r.Data!);
        Assert.Equal(2m, row.Available);
        Assert.Equal(2m, row.OnHand);
        Assert.Equal(8m, row.SuggestedQty);
        Assert.Equal(0m, row.IncomingOpenQty);
        Assert.Equal(0m, row.AvgDailySales);
        Assert.Null(row.DaysOfCover);
        Assert.Equal(40m, row.CostPrice);

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Incoming_OpenPo_Reduces_Suggested()
    {
        var (ctx, calc, tenantId, productId, warehouseId, supplierId, ownerId) =
            await SeedProductAsync(reorderMin: 10);
        await PostOpeningAsync(ctx, tenantId, productId, warehouseId, 2);

        var po = new PurchaseOrder
        {
            TenantId = tenantId,
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            Status = PurchaseOrderStatuses.Approved,
            OrderedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            ApprovedByUserId = ownerId,
            CreatedAtUtc = DateTime.UtcNow
        };
        po.Lines.Add(new PurchaseOrderLine
        {
            TenantId = tenantId,
            ProductId = productId,
            QtyOrdered = 5,
            QtyReceived = 0,
            UnitCost = 40,
            CreatedAtUtc = DateTime.UtcNow
        });
        ctx.PurchaseOrders.Add(po);
        await ctx.SaveChangesAsync();

        var r = await calc.CalculateAsync(tenantId);
        Assert.True(r.IsSuccess, r.Error);
        var row = Assert.Single(r.Data!);
        Assert.Equal(5m, row.IncomingOpenQty);
        Assert.Equal(3m, row.SuggestedQty); // max(0, 10-2-5)

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Incoming_InTransit_Transfer_Counts()
    {
        var (ctx, calc, tenantId, productId, warehouseId, _, ownerId) =
            await SeedProductAsync(reorderMin: 10);
        await PostOpeningAsync(ctx, tenantId, productId, warehouseId, 2);

        var otherWh = new Warehouse
        {
            TenantId = tenantId,
            Code = "SECOND",
            Name = "Second",
            IsDefault = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Warehouses.Add(otherWh);
        await ctx.SaveChangesAsync();

        var xfer = new StockTransfer
        {
            TenantId = tenantId,
            FromWarehouseId = otherWh.Id,
            ToWarehouseId = warehouseId,
            Status = StockTransferStatuses.InTransit,
            CreatedByUserId = ownerId,
            CreatedAtUtc = DateTime.UtcNow
        };
        xfer.Lines.Add(new StockTransferLine
        {
            TenantId = tenantId,
            ProductId = productId,
            Qty = 4,
            CreatedAtUtc = DateTime.UtcNow
        });
        ctx.StockTransfers.Add(xfer);
        await ctx.SaveChangesAsync();

        var r = await calc.CalculateAsync(tenantId);
        Assert.True(r.IsSuccess, r.Error);
        var row = Assert.Single(r.Data!);
        Assert.Equal(4m, row.IncomingOpenQty);
        Assert.Equal(4m, row.SuggestedQty); // 10 - 2 - 4

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Velocity_Raises_Suggested_Above_MinGap()
    {
        var (ctx, calc, tenantId, productId, warehouseId, _, _) =
            await SeedProductAsync(reorderMin: 1);
        await PostOpeningAsync(ctx, tenantId, productId, warehouseId, 2);

        // 60 sold over 30d => avgDaily 2; lead 7 => need ceil(14 - 2 - 0) = 12
        ctx.StockMovements.Add(new StockMovement
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            QtyDelta = -60,
            Reason = StockMovementReasons.Sale,
            ReferenceType = StockReferenceTypes.SaleLine,
            ReferenceId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var r = await calc.CalculateAsync(tenantId);
        Assert.True(r.IsSuccess, r.Error);
        var row = Assert.Single(r.Data!);
        Assert.Equal(2m, row.AvgDailySales);
        Assert.Equal(1.0m, row.DaysOfCover); // 2 / 2
        Assert.Equal(12m, row.SuggestedQty);

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task NoSuggestion_When_Covered_By_Stock_And_Incoming()
    {
        var (ctx, calc, tenantId, productId, warehouseId, supplierId, ownerId) =
            await SeedProductAsync(reorderMin: 10);
        await PostOpeningAsync(ctx, tenantId, productId, warehouseId, 8);

        var po = new PurchaseOrder
        {
            TenantId = tenantId,
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            Status = PurchaseOrderStatuses.Approved,
            OrderedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            ApprovedByUserId = ownerId,
            CreatedAtUtc = DateTime.UtcNow
        };
        po.Lines.Add(new PurchaseOrderLine
        {
            TenantId = tenantId,
            ProductId = productId,
            QtyOrdered = 5,
            QtyReceived = 0,
            UnitCost = 40,
            CreatedAtUtc = DateTime.UtcNow
        });
        ctx.PurchaseOrders.Add(po);
        await ctx.SaveChangesAsync();

        var r = await calc.CalculateAsync(tenantId);
        Assert.True(r.IsSuccess, r.Error);
        Assert.Empty(r.Data!);

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task OmitCost_WhenIncludeCostFalse()
    {
        var (ctx, calc, tenantId, productId, warehouseId, _, _) =
            await SeedProductAsync(reorderMin: 10);
        await PostOpeningAsync(ctx, tenantId, productId, warehouseId, 1);

        var open = await calc.CalculateAsync(tenantId, includeCost: false);
        Assert.True(open.IsSuccess, open.Error);
        Assert.Null(Assert.Single(open.Data!).CostPrice);

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Available_Excludes_Expired_Batches()
    {
        var (ctx, calc, tenantId, productId, warehouseId, _, _) =
            await SeedProductAsync(reorderMin: 10, trackExpiry: true);

        var expired = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = "E1",
            ExpiresOn = MembershipOperational.TodayCairo().AddDays(-1),
            CreatedAtUtc = DateTime.UtcNow
        };
        var fresh = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = "F1",
            ExpiresOn = MembershipOperational.TodayCairo().AddDays(30),
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.ProductBatches.AddRange(expired, fresh);
        await ctx.SaveChangesAsync();

        await PostOpeningAsync(ctx, tenantId, productId, warehouseId, 5, expired.Id);
        await PostOpeningAsync(ctx, tenantId, productId, warehouseId, 3, fresh.Id);

        var r = await calc.CalculateAsync(tenantId);
        Assert.True(r.IsSuccess, r.Error);
        var row = Assert.Single(r.Data!);
        Assert.Equal(8m, row.OnHand);
        Assert.Equal(3m, row.Available);
        Assert.Equal(7m, row.SuggestedQty); // 10 - 3

        await ctx.DisposeAsync();
    }
}
