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

public class StockAdjustmentServiceTests
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
        StockAdjustmentService svc,
        StockLedgerService ledger,
        Guid tenantId,
        Guid identityUserId,
        Guid productId,
        Guid warehouseId)> SeedAsync(bool trackExpiry = false, bool trackBatch = false)
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
        var warehouses = new WarehouseService(ctx, new NoOpAudit(), NullLogger<WarehouseService>.Instance);
        var svc = new StockAdjustmentService(
            ctx, ledger, warehouses, new NoOpAudit(), NullLogger<StockAdjustmentService>.Instance);
        return (ctx, svc, ledger, tenantId, identityUserId, product.Id, warehouse.Id);
    }

    [Fact]
    public async Task Opening_Plus50_ThenDamage_Minus2_Yields48()
    {
        var (_, svc, ledger, tenantId, identityUserId, productId, warehouseId) = await SeedAsync();

        var draft = await svc.CreateDraftAsync(tenantId, identityUserId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Opening,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = 50, UnitCost = 40 }
            }
        });
        Assert.True(draft.IsSuccess, draft.Error);
        Assert.Equal(2000m, draft.Data!.EstimatedValueImpactEgp);

        var posted = await svc.PostAsync(tenantId, identityUserId, draft.Data!.Id);
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(StockAdjustmentStatuses.Posted, posted.Data!.Status);

        var onHand = await ledger.GetOnHandAsync(tenantId, productId, warehouseId);
        Assert.Equal(50m, onHand.Data);

        var damageDraft = await svc.CreateDraftAsync(tenantId, identityUserId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Damage,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = -2 }
            }
        });
        Assert.True(damageDraft.IsSuccess, damageDraft.Error);
        Assert.Equal(50m, damageDraft.Data!.Lines[0].UnitCost); // defaulted from product

        var damagePosted = await svc.PostAsync(tenantId, identityUserId, damageDraft.Data!.Id);
        Assert.True(damagePosted.IsSuccess, damagePosted.Error);

        onHand = await ledger.GetOnHandAsync(tenantId, productId, warehouseId);
        Assert.Equal(48m, onHand.Data);

        var movements = await ledger.QueryStockAsync(tenantId, productId, warehouseId, includeMovements: true);
        Assert.Contains(movements.Data!.Movements!, m => m.Reason == StockMovementReasons.Opening && m.QtyDelta == 50);
        Assert.Contains(movements.Data.Movements!, m => m.Reason == StockMovementReasons.Adjustment && m.QtyDelta == -2);
    }

    [Fact]
    public async Task CancelDraft_Works_PostTwiceRejected()
    {
        var (_, svc, _, tenantId, identityUserId, productId, warehouseId) = await SeedAsync();

        var draft = await svc.CreateDraftAsync(tenantId, identityUserId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Opening,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = 10 }
            }
        });
        Assert.True(draft.IsSuccess, draft.Error);

        var cancelled = await svc.CancelAsync(tenantId, identityUserId, draft.Data!.Id);
        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(StockAdjustmentStatuses.Cancelled, cancelled.Data!.Status);

        var postCancelled = await svc.PostAsync(tenantId, identityUserId, draft.Data.Id);
        Assert.False(postCancelled.IsSuccess);

        var draft2 = await svc.CreateDraftAsync(tenantId, identityUserId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Opening,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = 5 }
            }
        });
        Assert.True((await svc.PostAsync(tenantId, identityUserId, draft2.Data!.Id)).IsSuccess);
        var postAgain = await svc.PostAsync(tenantId, identityUserId, draft2.Data.Id);
        Assert.False(postAgain.IsSuccess);
    }

    [Fact]
    public async Task NegativeOnHand_Blocked()
    {
        var (_, svc, _, tenantId, identityUserId, productId, warehouseId) = await SeedAsync();

        var draft = await svc.CreateDraftAsync(tenantId, identityUserId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Damage,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = -5 }
            }
        });
        Assert.True(draft.IsSuccess, draft.Error);

        var posted = await svc.PostAsync(tenantId, identityUserId, draft.Data!.Id);
        Assert.False(posted.IsSuccess);
        Assert.Contains("Insufficient", posted.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Opening_RejectsNegativeLine()
    {
        var (_, svc, _, tenantId, identityUserId, productId, warehouseId) = await SeedAsync();

        var draft = await svc.CreateDraftAsync(tenantId, identityUserId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Opening,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = -1 }
            }
        });
        Assert.False(draft.IsSuccess);
    }

    [Fact]
    public async Task Damage_RejectsPositiveQty()
    {
        var (_, svc, _, tenantId, identityUserId, productId, warehouseId) = await SeedAsync();
        var draft = await svc.CreateDraftAsync(tenantId, identityUserId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Damage,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = 3 }
            }
        });
        Assert.False(draft.IsSuccess);
        Assert.Contains("negative", draft.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Other_RequiresNote()
    {
        var (_, svc, _, tenantId, identityUserId, productId, warehouseId) = await SeedAsync();
        var draft = await svc.CreateDraftAsync(tenantId, identityUserId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Other,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = 1 }
            }
        });
        Assert.False(draft.IsSuccess);
    }

    [Fact]
    public async Task Expired_WriteOff_RequiresExpiredBatch_AndPreservesBatchOnLedger()
    {
        var (ctx, svc, ledger, tenantId, userId, productId, warehouseId) =
            await SeedAsync(trackExpiry: true, trackBatch: true);
        var today = MembershipOperational.TodayCairo();
        var expired = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = "E1",
            ExpiresOn = today.AddDays(-2),
            CreatedAtUtc = DateTime.UtcNow
        };
        var fresh = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = "F1",
            ExpiresOn = today.AddDays(20),
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.ProductBatches.AddRange(expired, fresh);
        await ctx.SaveChangesAsync();

        Assert.True((await ledger.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            BatchId = expired.Id,
            QtyDelta = 4,
            UnitCost = 50,
            Reason = StockMovementReasons.Opening,
            ReferenceType = "TestSeed",
            ReferenceId = Guid.NewGuid()
        })).IsSuccess);
        Assert.True((await ledger.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            BatchId = fresh.Id,
            QtyDelta = 3,
            UnitCost = 50,
            Reason = StockMovementReasons.Opening,
            ReferenceType = "TestSeed",
            ReferenceId = Guid.NewGuid()
        })).IsSuccess);

        var noBatch = await svc.CreateDraftAsync(tenantId, userId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Expired,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = -2 }
            }
        });
        Assert.False(noBatch.IsSuccess);

        var freshBatch = await svc.CreateDraftAsync(tenantId, userId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Expired,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = -2, BatchId = fresh.Id }
            }
        });
        Assert.False(freshBatch.IsSuccess);

        var draft = await svc.CreateDraftAsync(tenantId, userId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Expired,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = -2, BatchId = expired.Id }
            }
        });
        Assert.True(draft.IsSuccess, draft.Error);
        Assert.Equal(expired.Id, draft.Data!.Lines[0].BatchId);

        Assert.True((await svc.PostAsync(tenantId, userId, draft.Data.Id)).IsSuccess);
        Assert.Equal(2m, (await ledger.GetOnHandAsync(tenantId, productId, warehouseId, expired.Id)).Data);
        Assert.Equal(3m, (await ledger.GetOnHandAsync(tenantId, productId, warehouseId, fresh.Id)).Data);
        Assert.Equal(3m, (await ledger.GetAvailableAsync(tenantId, productId, warehouseId)).Data);
    }

    [Fact]
    public async Task InternalUse_PostsAdjustmentReason_WithBatchWhenTracked()
    {
        var (ctx, svc, ledger, tenantId, userId, productId, warehouseId) =
            await SeedAsync(trackBatch: true, trackExpiry: true);
        var batch = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = "B1",
            ExpiresOn = MembershipOperational.TodayCairo().AddDays(10),
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
            QtyDelta = 5,
            UnitCost = 50,
            Reason = StockMovementReasons.Opening,
            ReferenceType = "TestSeed",
            ReferenceId = Guid.NewGuid()
        })).IsSuccess);

        var draft = await svc.CreateDraftAsync(tenantId, userId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.InternalUse,
            Note = "Shake bar sample",
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = -1, BatchId = batch.Id }
            }
        });
        Assert.True(draft.IsSuccess, draft.Error);
        Assert.True((await svc.PostAsync(tenantId, userId, draft.Data!.Id)).IsSuccess);

        var mov = await ctx.StockMovements.SingleAsync(m =>
            m.Reason == StockMovementReasons.Adjustment && m.QtyDelta == -1);
        Assert.Equal(batch.Id, mov.BatchId);
        Assert.Equal(50m, mov.UnitCost);
        Assert.Contains("internal_use", mov.Note);
    }

    [Fact]
    public async Task SupplierCorrection_AllowsIncreaseAndDecrease()
    {
        var (_, svc, ledger, tenantId, userId, productId, warehouseId) = await SeedAsync();
        var up = await svc.CreateDraftAsync(tenantId, userId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.SupplierCorrection,
            Note = "GRN short",
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = 10 }
            }
        });
        Assert.True(up.IsSuccess, up.Error);
        Assert.True((await svc.PostAsync(tenantId, userId, up.Data!.Id)).IsSuccess);

        var down = await svc.CreateDraftAsync(tenantId, userId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.SupplierCorrection,
            Note = "GRN over",
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = -3 }
            }
        });
        Assert.True(down.IsSuccess, down.Error);
        Assert.True((await svc.PostAsync(tenantId, userId, down.Data!.Id)).IsSuccess);
        Assert.Equal(7m, (await ledger.GetOnHandAsync(tenantId, productId, warehouseId)).Data);
    }

    [Fact]
    public async Task Other_WithNote_Posts_AndRetryIsIdempotent()
    {
        var (_, svc, ledger, tenantId, userId, productId, warehouseId) = await SeedAsync();
        var draft = await svc.CreateDraftAsync(tenantId, userId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Other,
            Note = "Admin rebuild from paper log",
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = 4, UnitCost = 12 }
            }
        });
        Assert.True(draft.IsSuccess, draft.Error);
        Assert.Equal(48m, draft.Data!.EstimatedValueImpactEgp);

        Assert.True((await svc.PostAsync(tenantId, userId, draft.Data.Id)).IsSuccess);
        // Posted status blocks a second application; ledger would also no-op on Retry.
        Assert.False((await svc.PostAsync(tenantId, userId, draft.Data.Id)).IsSuccess);
        Assert.Equal(4m, (await ledger.GetOnHandAsync(tenantId, productId, warehouseId)).Data);
        Assert.Equal(1, (await ledger.QueryStockAsync(tenantId, productId, warehouseId, includeMovements: true))
            .Data!.Movements!.Count(m => m.Reason == StockMovementReasons.Adjustment));
    }

    [Fact]
    public async Task ConcurrentWriteOffs_SecondPostFails_WithoutNegativeBalance()
    {
        var (_, svc, ledger, tenantId, userId, productId, warehouseId) = await SeedAsync();
        Assert.True((await svc.PostAsync(tenantId, userId, (await svc.CreateDraftAsync(tenantId, userId,
            new CreateStockAdjustmentRequest
            {
                WarehouseId = warehouseId,
                ReasonCode = StockAdjustmentReasonCodes.Opening,
                Lines = new List<CreateStockAdjustmentLineRequest>
                {
                    new() { ProductId = productId, QtyDelta = 5 }
                }
            })).Data!.Id)).IsSuccess);

        var a = await svc.CreateDraftAsync(tenantId, userId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Damage,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = -4 }
            }
        });
        var b = await svc.CreateDraftAsync(tenantId, userId, new CreateStockAdjustmentRequest
        {
            WarehouseId = warehouseId,
            ReasonCode = StockAdjustmentReasonCodes.Lost,
            Lines = new List<CreateStockAdjustmentLineRequest>
            {
                new() { ProductId = productId, QtyDelta = -4 }
            }
        });
        Assert.True(a.IsSuccess && b.IsSuccess);

        Assert.True((await svc.PostAsync(tenantId, userId, a.Data!.Id)).IsSuccess);
        var second = await svc.PostAsync(tenantId, userId, b.Data!.Id);
        Assert.False(second.IsSuccess);
        Assert.Equal(1m, (await ledger.GetOnHandAsync(tenantId, productId, warehouseId)).Data);
        Assert.Equal(StockAdjustmentStatuses.Draft, (await svc.GetAsync(tenantId, b.Data.Id)).Data!.Status);
    }

    [Fact]
    public async Task ProductStockBreakdown_ExposesBatchBuckets_ForFixPicker()
    {
        var (ctx, _, ledger, tenantId, _, productId, warehouseId) =
            await SeedAsync(trackBatch: true, trackExpiry: true);
        var today = MembershipOperational.TodayCairo();
        var batch = new ProductBatch
        {
            TenantId = tenantId,
            ProductId = productId,
            BatchNumber = "PICK-1",
            ExpiresOn = today.AddDays(-1),
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
            QtyDelta = 2,
            UnitCost = 50,
            Reason = StockMovementReasons.Opening,
            ReferenceType = "TestSeed",
            ReferenceId = Guid.NewGuid()
        })).IsSuccess);

        var bd = await ledger.GetProductStockBreakdownAsync(tenantId, productId);
        Assert.True(bd.IsSuccess, bd.Error);
        Assert.Contains(bd.Data!.Batches, b =>
            b.BatchId == batch.Id && b.IsExpired && b.QtyOnHand == 2m && b.WarehouseId == warehouseId);
        Assert.Equal(0m, bd.Data.TotalAvailable);
        Assert.Equal(2m, bd.Data.TotalOnHand);
    }
}
