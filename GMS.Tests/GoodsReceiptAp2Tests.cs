namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

/// <summary>PAP AP-2 — goods receipts list for Buy docs hub.</summary>
public class GoodsReceiptAp2Tests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    [Fact]
    public async Task ListGoodsReceipts_ReturnsSupplierAndTotal()
    {
        var tenantId = Guid.NewGuid();
        var identityUserId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new GMS.Infrastructure.Services.TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        await using var ctx = new GymFlowProDbContext(options, tenantContext);

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
            Sku = "SKU1",
            Name = "Water",
            UnitOfMeasure = "pcs",
            SellPrice = 20,
            CostPrice = 10,
            Currency = "EGP",
            TrackStock = true,
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
            Name = "Acme",
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

        var created = await svc.CreateDraftAsync(tenantId, new CreatePurchaseOrderRequest
        {
            SupplierId = supplier.Id,
            WarehouseId = warehouse.Id,
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { ProductId = product.Id, QtyOrdered = 2, UnitCost = 10m }
            }
        });
        Assert.True(created.IsSuccess, created.Error);
        var approved = await svc.ApproveAsync(tenantId, identityUserId, created.Data!.Id);
        Assert.True(approved.IsSuccess, approved.Error);
        var lineId = approved.Data!.Lines[0].Id;
        var received = await svc.ReceiveAsync(tenantId, identityUserId, approved.Data.Id, new ReceivePurchaseOrderRequest
        {
            Lines = new List<ReceivePurchaseLineRequest>
            {
                new() { PurchaseOrderLineId = lineId, Qty = 2, UnitCost = 10m }
            }
        });
        Assert.True(received.IsSuccess, received.Error);

        var list = await svc.ListGoodsReceiptsAsync(tenantId);
        Assert.True(list.IsSuccess, list.Error);
        var row = Assert.Single(list.Data!.Items);
        Assert.Equal(received.Data!.Id, row.Id);
        Assert.Equal("Acme", row.SupplierName);
        Assert.Equal(20m, row.TotalAmount);
        Assert.Equal("received", row.Status);
        Assert.Equal("purchase_doc", row.DocKind);
    }
}
