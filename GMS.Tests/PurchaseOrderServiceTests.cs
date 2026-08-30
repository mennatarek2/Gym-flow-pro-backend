namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

public class PurchaseOrderServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static async Task<(
        GymFlowProDbContext ctx,
        PurchaseOrderService svc,
        IStockLedgerService ledger,
        Guid tenantId,
        Guid identityUserId,
        Guid productId,
        Guid warehouseId,
        Guid supplierId)> SeedAsync(bool trackBatch = false, bool trackExpiry = false)
    {
        var tenantId = Guid.NewGuid();
        var identityUserId = Guid.NewGuid();
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
            Address = "x",
            PhoneNumber = "01000000000",
            Email = $"{tenantId:N}@test.local",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        ctx.AppUsers.Add(new AppUser
        {
            TenantId = tenantId,
            UserId = identityUserId.ToString(),
            FirstName = "Owner",
            LastName = "One",
            Email = "owner@test.local",
            Role = "Owner",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });

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
            TrackBatch = trackBatch,
            TrackExpiry = trackExpiry,
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

        var ledger = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);
        var reorder = new InventoryReorderCalculator(ctx);
        var warehouses = new WarehouseService(ctx, new NoOpAudit(), NullLogger<WarehouseService>.Instance);
        var svc = new PurchaseOrderService(
            ctx, ledger, reorder, warehouses, new NoOpAudit(), NullLogger<PurchaseOrderService>.Instance);
        return (ctx, svc, ledger, tenantId, identityUserId, product.Id, warehouse.Id, supplier.Id);
    }

    private static async Task<PurchaseOrderDto> ApprovePoAsync(
        PurchaseOrderService svc, Guid tenantId, Guid userId, Guid supplierId, Guid warehouseId, Guid productId,
        decimal qty = 100, decimal cost = 50)
    {
        var created = await svc.CreateDraftAsync(tenantId, new CreatePurchaseOrderRequest
        {
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { ProductId = productId, QtyOrdered = qty, UnitCost = cost }
            }
        });
        Assert.True(created.IsSuccess, created.Error);
        var approved = await svc.ApproveAsync(tenantId, userId, created.Data!.Id);
        Assert.True(approved.IsSuccess, approved.Error);
        return approved.Data!;
    }

    [Fact]
    public async Task Partial_then_full_receive_updates_stock_and_status()
    {
        var (ctx, svc, ledger, tenantId, userId, productId, warehouseId, supplierId) = await SeedAsync();
        var po = await ApprovePoAsync(svc, tenantId, userId, supplierId, warehouseId, productId);

        var r1 = await svc.ReceiveAsync(tenantId, userId, po.Id, new ReceivePurchaseOrderRequest
        {
            Lines = new List<ReceivePurchaseLineRequest>
            {
                new() { PurchaseOrderLineId = po.Lines[0].Id, Qty = 40 }
            }
        });
        Assert.True(r1.IsSuccess, r1.Error);

        var mid = await svc.GetAsync(tenantId, po.Id);
        Assert.Equal(PurchaseOrderStatuses.PartiallyReceived, mid.Data!.Status);
        Assert.Equal(40, mid.Data.Lines[0].QtyReceived);

        var onHand = await ledger.GetOnHandAsync(tenantId, productId, warehouseId);
        Assert.True(onHand.IsSuccess);
        Assert.Equal(40, onHand.Data);

        var r2 = await svc.ReceiveAsync(tenantId, userId, po.Id, new ReceivePurchaseOrderRequest
        {
            Lines = new List<ReceivePurchaseLineRequest>
            {
                new() { PurchaseOrderLineId = po.Lines[0].Id, Qty = 60 }
            }
        });
        Assert.True(r2.IsSuccess, r2.Error);

        var done = await svc.GetAsync(tenantId, po.Id);
        Assert.Equal(PurchaseOrderStatuses.Received, done.Data!.Status);
        Assert.Equal(100, done.Data.Lines[0].QtyReceived);

        onHand = await ledger.GetOnHandAsync(tenantId, productId, warehouseId);
        Assert.Equal(100, onHand.Data);

        var product = await ctx.Products.FindAsync(productId);
        Assert.Equal(50, product!.CostPrice);
    }

    [Fact]
    public async Task Over_receive_rejected()
    {
        var (_, svc, _, tenantId, userId, productId, warehouseId, supplierId) = await SeedAsync();
        var po = await ApprovePoAsync(svc, tenantId, userId, supplierId, warehouseId, productId, qty: 10);

        var bad = await svc.ReceiveAsync(tenantId, userId, po.Id, new ReceivePurchaseOrderRequest
        {
            Lines = new List<ReceivePurchaseLineRequest>
            {
                new() { PurchaseOrderLineId = po.Lines[0].Id, Qty = 11 }
            }
        });
        Assert.False(bad.IsSuccess);
        Assert.Contains("Over-receive", bad.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancel_after_partial_rejected()
    {
        var (_, svc, _, tenantId, userId, productId, warehouseId, supplierId) = await SeedAsync();
        var po = await ApprovePoAsync(svc, tenantId, userId, supplierId, warehouseId, productId);

        var r1 = await svc.ReceiveAsync(tenantId, userId, po.Id, new ReceivePurchaseOrderRequest
        {
            Lines = new List<ReceivePurchaseLineRequest>
            {
                new() { PurchaseOrderLineId = po.Lines[0].Id, Qty = 10 }
            }
        });
        Assert.True(r1.IsSuccess, r1.Error);

        var cancel = await svc.CancelAsync(tenantId, po.Id);
        Assert.False(cancel.IsSuccess);
        Assert.Contains("partial", cancel.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Batch_and_expiry_required_when_flags_set()
    {
        var (ctx, svc, ledger, tenantId, userId, productId, warehouseId, supplierId) =
            await SeedAsync(trackBatch: true, trackExpiry: true);
        var po = await ApprovePoAsync(svc, tenantId, userId, supplierId, warehouseId, productId, qty: 5, cost: 55);

        var missing = await svc.ReceiveAsync(tenantId, userId, po.Id, new ReceivePurchaseOrderRequest
        {
            Lines = new List<ReceivePurchaseLineRequest>
            {
                new() { PurchaseOrderLineId = po.Lines[0].Id, Qty = 5 }
            }
        });
        Assert.False(missing.IsSuccess);

        var ok = await svc.ReceiveAsync(tenantId, userId, po.Id, new ReceivePurchaseOrderRequest
        {
            Lines = new List<ReceivePurchaseLineRequest>
            {
                new()
                {
                    PurchaseOrderLineId = po.Lines[0].Id,
                    Qty = 5,
                    BatchNumber = "LOT-A",
                    ExpiresOn = new DateOnly(2027, 1, 15)
                }
            }
        });
        Assert.True(ok.IsSuccess, ok.Error);
        Assert.NotNull(ok.Data!.Lines[0].ProductBatchId);

        var batch = await ctx.ProductBatches.SingleAsync();
        Assert.Equal("LOT-A", batch.BatchNumber);
        Assert.Equal(new DateOnly(2027, 1, 15), batch.ExpiresOn);

        var onHand = await ledger.GetOnHandAsync(tenantId, productId, warehouseId, batch.Id);
        Assert.Equal(5, onHand.Data);
    }

    [Fact]
    public async Task Supplier_isolation_across_tenants()
    {
        var (ctxA, svcA, _, tenantA, userA, productA, whA, _) = await SeedAsync();
        var tenantB = Guid.NewGuid();
        ctxA.Tenants.Add(new Tenant
        {
            Id = tenantB,
            Name = "Other",
            NameAr = "أخرى",
            GymCode = $"T-{tenantB:N}"[..12],
            City = "Alex",
            Address = "y",
            PhoneNumber = "01111111111",
            Email = $"{tenantB:N}@test.local",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        var foreignSupplier = new Supplier
        {
            TenantId = tenantB,
            Name = "Foreign",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctxA.Suppliers.Add(foreignSupplier);
        await ctxA.SaveChangesAsync();

        var bad = await svcA.CreateDraftAsync(tenantA, new CreatePurchaseOrderRequest
        {
            SupplierId = foreignSupplier.Id,
            WarehouseId = whA,
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { ProductId = productA, QtyOrdered = 1, UnitCost = 1 }
            }
        });
        Assert.False(bad.IsSuccess);
    }

    [Fact]
    public async Task CreateDraft_AllowsSupplierDifferentFromProductDefault()
    {
        var (ctx, svc, _, tenantId, _, productId, warehouseId, defaultSupplierId) = await SeedAsync();
        var other = new Supplier
        {
            TenantId = tenantId,
            Name = "XYZ Sports",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Suppliers.Add(other);
        var product = await ctx.Products.FindAsync(productId);
        product!.DefaultSupplierId = defaultSupplierId;
        await ctx.SaveChangesAsync();

        var created = await svc.CreateDraftAsync(tenantId, new CreatePurchaseOrderRequest
        {
            SupplierId = other.Id,
            WarehouseId = warehouseId,
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { ProductId = productId, QtyOrdered = 2, UnitCost = 40 }
            }
        });
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(other.Id, created.Data!.SupplierId);
    }

    [Fact]
    public async Task CreateDraft_SucceedsWhenProductHasNoDefaultSupplier()
    {
        var (_, svc, _, tenantId, _, productId, warehouseId, supplierId) = await SeedAsync();
        var created = await svc.CreateDraftAsync(tenantId, new CreatePurchaseOrderRequest
        {
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { ProductId = productId, QtyOrdered = 1, UnitCost = 10 }
            }
        });
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(supplierId, created.Data!.SupplierId);
    }

    [Fact]
    public async Task ChangingProductDefaultSupplier_DoesNotRewriteExistingPurchase()
    {
        var (ctx, svc, _, tenantId, _, productId, warehouseId, supplierId) = await SeedAsync();
        var created = await svc.CreateDraftAsync(tenantId, new CreatePurchaseOrderRequest
        {
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { ProductId = productId, QtyOrdered = 1, UnitCost = 10 }
            }
        });
        Assert.True(created.IsSuccess, created.Error);

        var later = new Supplier
        {
            TenantId = tenantId,
            Name = "Later Default",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Suppliers.Add(later);
        var product = await ctx.Products.FindAsync(productId);
        product!.DefaultSupplierId = later.Id;
        await ctx.SaveChangesAsync();

        var po = await svc.GetAsync(tenantId, created.Data!.Id);
        Assert.True(po.IsSuccess, po.Error);
        Assert.Equal(supplierId, po.Data!.SupplierId);
        Assert.NotEqual(later.Id, po.Data.SupplierId);
    }
}
