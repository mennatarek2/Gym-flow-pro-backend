namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class StockCountServiceTests
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
        StockCountService svc,
        IStockLedgerService ledger,
        Guid tenantId,
        Guid identityUserId,
        Guid productId,
        Guid warehouseId)> SeedAsync(decimal onHand = 50)
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
                Reason = StockMovementReasons.Opening,
                ReferenceType = "TestSeed",
                ReferenceId = Guid.NewGuid()
            })).IsSuccess);
        }

        var svc = new StockCountService(
            ctx, ledger, new NoOpAudit(), NullLogger<StockCountService>.Instance);
        return (ctx, svc, ledger, tenantId, identityUserId, product.Id, warehouse.Id);
    }

    private static async Task<StockCountDto> PrepareSubmittedAsync(
        StockCountService svc, Guid tenantId, Guid userId, Guid warehouseId, Guid productId, decimal countedQty)
    {
        var created = await svc.CreateAsync(tenantId, userId, new CreateStockCountRequest
        {
            WarehouseId = warehouseId,
            ProductIds = new List<Guid> { productId }
        });
        Assert.True(created.IsSuccess, created.Error);

        var updated = await svc.UpdateLinesAsync(tenantId, created.Data!.Id, new UpdateStockCountLinesRequest
        {
            Lines = new List<UpdateStockCountLineRequest>
            {
                new() { LineId = created.Data.Lines[0].Id, CountedQty = countedQty }
            }
        });
        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal(countedQty - created.Data.Lines[0].SystemQty, updated.Data!.Lines[0].Variance);

        var submitted = await svc.SubmitAsync(tenantId, userId, created.Data.Id);
        Assert.True(submitted.IsSuccess, submitted.Error);
        return submitted.Data!;
    }

    [Fact]
    public async Task Approve_Variance_PostsCountMovement()
    {
        var (ctx, svc, ledger, tenantId, userId, productId, warehouseId) = await SeedAsync(50);
        var count = await PrepareSubmittedAsync(svc, tenantId, userId, warehouseId, productId, countedQty: 45);

        var approved = await svc.ApproveAsync(tenantId, userId, count.Id);
        Assert.True(approved.IsSuccess, approved.Error);
        Assert.Equal(StockCountStatuses.Approved, approved.Data!.Status);
        Assert.Equal(45, (await ledger.GetOnHandAsync(tenantId, productId, warehouseId)).Data);

        var move = await ctx.StockMovements.SingleAsync(m =>
            m.Reason == StockMovementReasons.Count && m.ProductId == productId);
        Assert.Equal(-5, move.QtyDelta);
        Assert.Equal(StockReferenceTypes.StockCount, move.ReferenceType);
    }

    [Fact]
    public async Task Approve_ZeroVariance_NoMovements()
    {
        var (ctx, svc, _, tenantId, userId, productId, warehouseId) = await SeedAsync(50);
        var count = await PrepareSubmittedAsync(svc, tenantId, userId, warehouseId, productId, countedQty: 50);

        var approved = await svc.ApproveAsync(tenantId, userId, count.Id);
        Assert.True(approved.IsSuccess, approved.Error);
        Assert.False(await ctx.StockMovements.AnyAsync(m => m.Reason == StockMovementReasons.Count));
    }

    [Fact]
    public async Task Approve_AfterDrift_Fails()
    {
        var (_, svc, ledger, tenantId, userId, productId, warehouseId) = await SeedAsync(50);
        var count = await PrepareSubmittedAsync(svc, tenantId, userId, warehouseId, productId, countedQty: 45);

        // Mid-count sale / adjustment drifts live qty
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

        var approved = await svc.ApproveAsync(tenantId, userId, count.Id);
        Assert.False(approved.IsSuccess);
        Assert.Contains("drifted", approved.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DoubleApprove_Rejected()
    {
        var (_, svc, _, tenantId, userId, productId, warehouseId) = await SeedAsync(50);
        var count = await PrepareSubmittedAsync(svc, tenantId, userId, warehouseId, productId, countedQty: 45);

        Assert.True((await svc.ApproveAsync(tenantId, userId, count.Id)).IsSuccess);
        var again = await svc.ApproveAsync(tenantId, userId, count.Id);
        Assert.False(again.IsSuccess);
        Assert.Contains("approve", again.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_RejectsTrackBatchProducts()
    {
        var (ctx, svc, _, tenantId, userId, productId, warehouseId) = await SeedAsync(10);
        var product = await ctx.Products.SingleAsync(p => p.Id == productId);
        product.TrackBatch = true;
        product.TrackExpiry = true;
        await ctx.SaveChangesAsync();

        var created = await svc.CreateAsync(tenantId, userId, new CreateStockCountRequest
        {
            WarehouseId = warehouseId,
            ProductIds = new List<Guid> { productId }
        });
        Assert.False(created.IsSuccess);
        Assert.Contains("batch", created.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
