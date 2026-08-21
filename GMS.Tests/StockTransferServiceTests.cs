namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class StockTransferServiceTests
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
        StockTransferService svc,
        IStockLedgerService ledger,
        Guid tenantId,
        Guid identityUserId,
        Guid productId,
        Guid fromId,
        Guid toId)> SeedAsync(decimal fromQty = 10, bool trackExpiry = false, bool trackBatch = false)
    {
        var tenantId = Guid.NewGuid();
        var identityUserId = Guid.NewGuid();
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
            CostPrice = 50,
            Currency = "EGP",
            TrackStock = true,
            TrackBatch = trackBatch,
            TrackExpiry = trackExpiry,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var from = new Warehouse
        {
            TenantId = tenantId,
            Code = "MAIN",
            Name = "Main",
            IsDefault = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var to = new Warehouse
        {
            TenantId = tenantId,
            Code = "DESK",
            Name = "Front Desk",
            IsDefault = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Products.Add(product);
        ctx.Warehouses.Add(from);
        ctx.Warehouses.Add(to);
        await ctx.SaveChangesAsync();

        var ledger = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);
        if (fromQty > 0 && !trackExpiry && !trackBatch)
        {
            var open = await ledger.PostAsync(new StockLedgerPostRequest
            {
                TenantId = tenantId,
                ProductId = product.Id,
                WarehouseId = from.Id,
                QtyDelta = fromQty,
                UnitCost = 50,
                Reason = StockMovementReasons.Opening,
                ReferenceType = "TestSeed",
                ReferenceId = Guid.NewGuid()
            });
            Assert.True(open.IsSuccess, open.Error);
        }

        var svc = new StockTransferService(
            ctx, ledger, new NoOpAudit(), NullLogger<StockTransferService>.Instance);
        return (ctx, svc, ledger, tenantId, identityUserId, product.Id, from.Id, to.Id);
    }

    private static async Task<ProductBatch> AddBatchStockAsync(
        GymFlowProDbContext ctx,
        IStockLedgerService ledger,
        Guid tenantId,
        Guid productId,
        Guid warehouseId,
        string batchNumber,
        DateOnly? expiresOn,
        decimal qty)
    {
        var batch = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = batchNumber,
            ExpiresOn = expiresOn,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.ProductBatches.Add(batch);
        await ctx.SaveChangesAsync();

        var open = await ledger.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            BatchId = batch.Id,
            QtyDelta = qty,
            UnitCost = 50,
            Reason = StockMovementReasons.Opening,
            ReferenceType = "TestSeed",
            ReferenceId = Guid.NewGuid()
        });
        Assert.True(open.IsSuccess, open.Error);
        return batch;
    }

    [Fact]
    public async Task HappyPath_SubmitThenReceive_ConservesStock()
    {
        var (_, svc, ledger, tenantId, userId, productId, fromId, toId) = await SeedAsync(10);

        var created = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest>
            {
                new() { ProductId = productId, Qty = 4 }
            }
        });
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(StockTransferStatuses.Pending, created.Data!.Status);

        var submitted = await svc.SubmitAsync(tenantId, userId, created.Data.Id);
        Assert.True(submitted.IsSuccess, submitted.Error);
        Assert.Equal(StockTransferStatuses.InTransit, submitted.Data!.Status);
        Assert.Equal(6, (await ledger.GetOnHandAsync(tenantId, productId, fromId)).Data);
        Assert.Equal(0, (await ledger.GetOnHandAsync(tenantId, productId, toId)).Data);

        var received = await svc.ReceiveAsync(tenantId, userId, created.Data.Id);
        Assert.True(received.IsSuccess, received.Error);
        Assert.Equal(StockTransferStatuses.Completed, received.Data!.Status);
        Assert.Equal(6, (await ledger.GetOnHandAsync(tenantId, productId, fromId)).Data);
        Assert.Equal(4, (await ledger.GetOnHandAsync(tenantId, productId, toId)).Data);

        var again = await svc.ReceiveAsync(tenantId, userId, created.Data.Id);
        Assert.False(again.IsSuccess);
    }

    [Fact]
    public async Task Submit_InsufficientSource_Rejected()
    {
        var (_, svc, _, tenantId, userId, productId, fromId, toId) = await SeedAsync(2);

        var created = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest>
            {
                new() { ProductId = productId, Qty = 5 }
            }
        });
        Assert.True(created.IsSuccess, created.Error);

        var submitted = await svc.SubmitAsync(tenantId, userId, created.Data!.Id);
        Assert.False(submitted.IsSuccess);
        Assert.Contains("Insufficient", submitted.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancel_BeforeSubmit_NoStockMovement()
    {
        var (ctx, svc, ledger, tenantId, userId, productId, fromId, toId) = await SeedAsync(10);

        var created = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest>
            {
                new() { ProductId = productId, Qty = 3 }
            }
        });
        Assert.True(created.IsSuccess, created.Error);

        var cancelled = await svc.CancelAsync(tenantId, userId, created.Data!.Id);
        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(StockTransferStatuses.Cancelled, cancelled.Data!.Status);
        Assert.Equal(10, (await ledger.GetOnHandAsync(tenantId, productId, fromId)).Data);
        Assert.False(await ctx.StockMovements.AnyAsync(m =>
            m.Reason == StockMovementReasons.TransferOut || m.Reason == StockMovementReasons.TransferIn));
    }

    [Fact]
    public async Task Reject_InTransit_ReturnsStockToSource()
    {
        var (_, svc, ledger, tenantId, userId, productId, fromId, toId) = await SeedAsync(10);

        var created = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest>
            {
                new() { ProductId = productId, Qty = 3 }
            }
        });
        Assert.True((await svc.SubmitAsync(tenantId, userId, created.Data!.Id)).IsSuccess);

        var rejected = await svc.RejectAsync(tenantId, userId, created.Data.Id);
        Assert.True(rejected.IsSuccess, rejected.Error);
        Assert.Equal(StockTransferStatuses.Cancelled, rejected.Data!.Status);
        Assert.Equal(10, (await ledger.GetOnHandAsync(tenantId, productId, fromId)).Data);
        Assert.Equal(0, (await ledger.GetOnHandAsync(tenantId, productId, toId)).Data);
    }

    [Fact]
    public async Task Submit_Fefo_SkipsExpired_AndExpandsLines()
    {
        var (ctx, svc, ledger, tenantId, userId, productId, fromId, toId) =
            await SeedAsync(0, trackExpiry: true, trackBatch: true);

        var today = MembershipOperational.TodayCairo();
        var expired = await AddBatchStockAsync(ctx, ledger, tenantId, productId, fromId, "E1", today.AddDays(-1), 5);
        var soon = await AddBatchStockAsync(ctx, ledger, tenantId, productId, fromId, "S1", today.AddDays(5), 2);
        var later = await AddBatchStockAsync(ctx, ledger, tenantId, productId, fromId, "L1", today.AddDays(30), 4);

        var created = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest>
            {
                new() { ProductId = productId, Qty = 3 }
            }
        });
        Assert.True(created.IsSuccess, created.Error);
        Assert.Single(created.Data!.Lines);
        Assert.Null(created.Data.Lines[0].BatchId);

        var submitted = await svc.SubmitAsync(tenantId, userId, created.Data.Id);
        Assert.True(submitted.IsSuccess, submitted.Error);
        Assert.Single(submitted.Data!.Lines); // lines stay aggregated; FEFO is on movements
        Assert.Null(submitted.Data.Lines[0].BatchId);

        var outs = await ctx.StockMovements
            .Where(m => m.Reason == StockMovementReasons.TransferOut)
            .ToListAsync();
        Assert.Equal(2, outs.Count);
        Assert.Equal(2m, outs.Where(m => m.BatchId == soon.Id).Sum(m => -m.QtyDelta));
        Assert.Equal(1m, outs.Where(m => m.BatchId == later.Id).Sum(m => -m.QtyDelta));
        Assert.DoesNotContain(outs, m => m.BatchId == expired.Id);

        Assert.Equal(5m, (await ledger.GetOnHandAsync(tenantId, productId, fromId, expired.Id)).Data);
        Assert.Equal(0m, (await ledger.GetOnHandAsync(tenantId, productId, fromId, soon.Id)).Data);
        Assert.Equal(3m, (await ledger.GetOnHandAsync(tenantId, productId, fromId, later.Id)).Data);

        var received = await svc.ReceiveAsync(tenantId, userId, created.Data.Id);
        Assert.True(received.IsSuccess, received.Error);
        Assert.Equal(2m, (await ledger.GetOnHandAsync(tenantId, productId, toId, soon.Id)).Data);
        Assert.Equal(1m, (await ledger.GetOnHandAsync(tenantId, productId, toId, later.Id)).Data);
        Assert.Equal(0m, (await ledger.GetOnHandAsync(tenantId, productId, toId, expired.Id)).Data);
        Assert.Equal(3m, (await ledger.GetAvailableAsync(tenantId, productId, toId)).Data);
    }

    [Fact]
    public async Task Submit_ExpiredOnly_InsufficientSellable()
    {
        var (ctx, svc, ledger, tenantId, userId, productId, fromId, toId) =
            await SeedAsync(0, trackExpiry: true, trackBatch: true);
        var today = MembershipOperational.TodayCairo();
        await AddBatchStockAsync(ctx, ledger, tenantId, productId, fromId, "E1", today.AddDays(-2), 10);

        var created = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest>
            {
                new() { ProductId = productId, Qty = 1 }
            }
        });
        Assert.True(created.IsSuccess, created.Error);

        var submitted = await svc.SubmitAsync(tenantId, userId, created.Data!.Id);
        Assert.False(submitted.IsSuccess);
        Assert.Contains("sellable", submitted.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10m, (await ledger.GetOnHandAsync(tenantId, productId, fromId)).Data);
    }

    [Fact]
    public async Task Submit_ExplicitExpiredBatchId_Rejected()
    {
        var (ctx, svc, ledger, tenantId, userId, productId, fromId, toId) =
            await SeedAsync(0, trackExpiry: true, trackBatch: true);
        var today = MembershipOperational.TodayCairo();
        var expired = await AddBatchStockAsync(ctx, ledger, tenantId, productId, fromId, "E1", today.AddDays(-1), 4);
        await AddBatchStockAsync(ctx, ledger, tenantId, productId, fromId, "F1", today.AddDays(10), 4);

        var created = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest>
            {
                new() { ProductId = productId, Qty = 2, BatchId = expired.Id }
            }
        });
        Assert.True(created.IsSuccess, created.Error);

        var submitted = await svc.SubmitAsync(tenantId, userId, created.Data!.Id);
        Assert.False(submitted.IsSuccess);
        Assert.Contains("expired", submitted.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reject_AfterFefo_RestoresSameBatches()
    {
        var (ctx, svc, ledger, tenantId, userId, productId, fromId, toId) =
            await SeedAsync(0, trackExpiry: true, trackBatch: true);
        var today = MembershipOperational.TodayCairo();
        var soon = await AddBatchStockAsync(ctx, ledger, tenantId, productId, fromId, "S1", today.AddDays(3), 2);
        var later = await AddBatchStockAsync(ctx, ledger, tenantId, productId, fromId, "L1", today.AddDays(40), 5);

        var created = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest>
            {
                new() { ProductId = productId, Qty = 3 }
            }
        });
        Assert.True((await svc.SubmitAsync(tenantId, userId, created.Data!.Id)).IsSuccess);

        var rejected = await svc.RejectAsync(tenantId, userId, created.Data.Id);
        Assert.True(rejected.IsSuccess, rejected.Error);
        Assert.Equal(2m, (await ledger.GetOnHandAsync(tenantId, productId, fromId, soon.Id)).Data);
        Assert.Equal(5m, (await ledger.GetOnHandAsync(tenantId, productId, fromId, later.Id)).Data);
        Assert.Equal(0m, (await ledger.GetOnHandAsync(tenantId, productId, toId)).Data);
    }

    [Fact]
    public async Task TransferMovements_CarryUnitCost()
    {
        var (ctx, svc, _, tenantId, userId, productId, fromId, toId) = await SeedAsync(5);

        var created = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest>
            {
                new() { ProductId = productId, Qty = 2 }
            }
        });
        Assert.True((await svc.SubmitAsync(tenantId, userId, created.Data!.Id)).IsSuccess);
        Assert.True((await svc.ReceiveAsync(tenantId, userId, created.Data.Id)).IsSuccess);

        var outs = await ctx.StockMovements.Where(m => m.Reason == StockMovementReasons.TransferOut).ToListAsync();
        var ins = await ctx.StockMovements.Where(m =>
            m.Reason == StockMovementReasons.TransferIn
            && m.ReferenceType == StockReferenceTypes.StockTransferLine).ToListAsync();
        Assert.All(outs, m => Assert.Equal(50m, m.UnitCost));
        Assert.All(ins, m => Assert.Equal(50m, m.UnitCost));
    }

    [Fact]
    public async Task ConcurrentSubmit_SecondFails_WhenStockExhausted()
    {
        var (_, svc, ledger, tenantId, userId, productId, fromId, toId) = await SeedAsync(5);

        var a = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest> { new() { ProductId = productId, Qty = 4 } }
        });
        var b = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest> { new() { ProductId = productId, Qty = 4 } }
        });
        Assert.True(a.IsSuccess && b.IsSuccess);

        var first = await svc.SubmitAsync(tenantId, userId, a.Data!.Id);
        var second = await svc.SubmitAsync(tenantId, userId, b.Data!.Id);
        Assert.True(first.IsSuccess, first.Error);
        Assert.False(second.IsSuccess);
        Assert.Equal(1m, (await ledger.GetOnHandAsync(tenantId, productId, fromId)).Data);
    }

    [Fact]
    public async Task PartialShipReceive_NotSupported_StatusGuards()
    {
        // Documented G2 debt: model is all-or-nothing ship/receive — no partial APIs.
        var (_, svc, _, tenantId, userId, productId, fromId, toId) = await SeedAsync(10);
        var created = await svc.CreatePendingAsync(tenantId, userId, new CreateStockTransferRequest
        {
            FromWarehouseId = fromId,
            ToWarehouseId = toId,
            Lines = new List<CreateStockTransferLineRequest> { new() { ProductId = productId, Qty = 4 } }
        });
        Assert.False((await svc.ReceiveAsync(tenantId, userId, created.Data!.Id)).IsSuccess);
        Assert.True((await svc.SubmitAsync(tenantId, userId, created.Data.Id)).IsSuccess);
        Assert.False((await svc.SubmitAsync(tenantId, userId, created.Data.Id)).IsSuccess);
        Assert.False((await svc.CancelAsync(tenantId, userId, created.Data.Id)).IsSuccess);
    }
}
