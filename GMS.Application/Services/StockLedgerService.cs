namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>INVS-3 stock ledger — only writer of movements and balances.</summary>
public class StockLedgerService : IStockLedgerService
{
    private readonly GymFlowProDbContext _db;
    private readonly ILogger<StockLedgerService> _logger;

    public StockLedgerService(GymFlowProDbContext db, ILogger<StockLedgerService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<StockMovementDto>> PostAsync(
        StockLedgerPostRequest request, CancellationToken ct = default)
    {
        if (request.QtyDelta == 0)
            return Result<StockMovementDto>.Failure("QtyDelta cannot be zero / لا يمكن أن تكون الكمية صفر");

        if (string.IsNullOrWhiteSpace(request.Reason)
            || !StockMovementReasons.All.Contains(request.Reason.Trim()))
            return Result<StockMovementDto>.Failure("Invalid stock movement reason / سبب حركة المخزون غير صالح");

        var reason = request.Reason.Trim().ToLowerInvariant();
        var refType = string.IsNullOrWhiteSpace(request.ReferenceType)
            ? null
            : request.ReferenceType.Trim();

        if (request.ReferenceId.HasValue && refType != null)
        {
            var existing = await _db.StockMovements
                .AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.TenantId == request.TenantId
                    && m.ReferenceType == refType
                    && m.ReferenceId == request.ReferenceId
                    && m.Reason == reason
                    && m.BatchId == request.BatchId, ct);
            if (existing != null)
                return Result<StockMovementDto>.Success(MapMovement(existing));
        }

        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.TenantId == request.TenantId, ct);
        if (product == null)
            return Result<StockMovementDto>.Failure("Product not found / المنتج غير موجود");
        if (!product.IsActive || product.IsArchived)
            return Result<StockMovementDto>.Failure("Product is not active / المنتج غير نشط");
        if (!product.TrackStock)
            return Result<StockMovementDto>.Failure(
                "Product does not track stock / المنتج لا يتتبع المخزون");

        if (!product.AllowFractionalQty
            && decimal.Truncate(request.QtyDelta) != request.QtyDelta)
            return Result<StockMovementDto>.Failure(
                "Fractional quantity not allowed for this product / الكسور غير مسموحة لهذا المنتج");

        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId && w.TenantId == request.TenantId, ct);
        if (warehouse == null)
            return Result<StockMovementDto>.Failure("Warehouse not found / المخزن غير موجود");
        if (!warehouse.IsActive)
            return Result<StockMovementDto>.Failure("Warehouse is inactive / المخزن غير نشط");

        if (request.BatchId.HasValue && !product.TrackBatch && !product.TrackExpiry)
            return Result<StockMovementDto>.Failure(
                "Product does not track batches / المنتج لا يتتبع التشغيلات");

        // Critical Close C1: retry lost updates when this Post owns the transaction.
        const int maxAttempts = 3;
        var ownsTransaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction == null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
            if (ownsTransaction)
                tx = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                if (request.ReferenceId.HasValue && refType != null)
                {
                    var existing = await _db.StockMovements
                        .FirstOrDefaultAsync(m =>
                            m.TenantId == request.TenantId
                            && m.ReferenceType == refType
                            && m.ReferenceId == request.ReferenceId
                            && m.Reason == reason
                            && m.BatchId == request.BatchId, ct);
                    if (existing != null)
                    {
                        if (tx != null) await tx.CommitAsync(ct);
                        return Result<StockMovementDto>.Success(MapMovement(existing));
                    }
                }

                var balance = await GetOrCreateBalanceAsync(
                    request.TenantId, request.ProductId, request.WarehouseId, request.BatchId, ct);

                // Re-read token after create flush races.
                if (_db.Entry(balance).State == EntityState.Unchanged)
                    await _db.Entry(balance).ReloadAsync(ct);

                var newQty = balance.QtyOnHand + request.QtyDelta;
                if (newQty < 0)
                {
                    if (tx != null) await tx.RollbackAsync(ct);
                    return Result<StockMovementDto>.Failure(
                        $"Insufficient stock (on hand {balance.QtyOnHand}, delta {request.QtyDelta}) / رصيد غير كافٍ");
                }

                var now = DateTime.UtcNow;
                var movement = new StockMovement
                {
                    TenantId = request.TenantId,
                    ProductId = request.ProductId,
                    WarehouseId = request.WarehouseId,
                    BatchId = request.BatchId,
                    QtyDelta = request.QtyDelta,
                    UnitCost = request.UnitCost,
                    Reason = reason,
                    ReferenceType = refType,
                    ReferenceId = request.ReferenceId,
                    Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                    OccurredAtUtc = request.OccurredAtUtc ?? now,
                    CreatedByUserId = request.CreatedByUserId,
                    CreatedAtUtc = now
                };

                balance.QtyOnHand = newQty;
                balance.UpdatedAtUtc = now;

                _db.StockMovements.Add(movement);
                await _db.SaveChangesAsync(ct);

                if (tx != null)
                    await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "Stock post {Reason} product {ProductId} wh {WarehouseId} delta {Delta} → {OnHand}",
                    reason, request.ProductId, request.WarehouseId, request.QtyDelta, newQty);

                return Result<StockMovementDto>.Success(MapMovement(movement));
            }
            catch (DbUpdateConcurrencyException) when (ownsTransaction && attempt < maxAttempts)
            {
                if (tx != null) await tx.RollbackAsync(ct);
                DetachPendingStockWrites();
                _logger.LogWarning(
                    "Stock balance concurrency conflict attempt {Attempt}/{Max} product {ProductId}",
                    attempt, maxAttempts, request.ProductId);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (tx != null) await tx.RollbackAsync(ct);
                DetachPendingStockWrites();
                return Result<StockMovementDto>.Failure(
                    "Concurrent stock update — retry / تحديث مخزون متزامن — أعد المحاولة");
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex) && request.ReferenceId.HasValue && refType != null)
            {
                if (tx != null) await tx.RollbackAsync(ct);
                DetachPendingStockWrites();
                var raced = await _db.StockMovements.AsNoTracking()
                    .FirstOrDefaultAsync(m =>
                        m.TenantId == request.TenantId
                        && m.ReferenceType == refType
                        && m.ReferenceId == request.ReferenceId
                        && m.Reason == reason
                        && m.BatchId == request.BatchId, ct);
                if (raced != null)
                    return Result<StockMovementDto>.Success(MapMovement(raced));
                return Result<StockMovementDto>.Failure(
                    "Concurrent stock post conflict / تعارض في تسجيل حركة المخزون");
            }
            catch (Exception ex)
            {
                if (tx != null) await tx.RollbackAsync(ct);
                DetachPendingStockWrites();
                _logger.LogError(ex, "Stock post failed for product {ProductId}", request.ProductId);
                return Result<StockMovementDto>.Failure(
                    "Failed to post stock movement / فشل تسجيل حركة المخزون", ex.Message);
            }
            finally
            {
                if (tx != null)
                    await tx.DisposeAsync();
            }
        }

        return Result<StockMovementDto>.Failure(
            "Concurrent stock update — retry / تحديث مخزون متزامن — أعد المحاولة");
    }

    private void DetachPendingStockWrites()
    {
        foreach (var entry in _db.ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                     .ToList())
        {
            if (entry.Entity is StockMovement or StockBalance)
                entry.State = EntityState.Detached;
        }
    }

    public async Task<Result<decimal>> GetOnHandAsync(
        Guid tenantId, Guid productId, Guid warehouseId, Guid? batchId = null, CancellationToken ct = default)
    {
        if (batchId.HasValue)
        {
            var bal = await _db.StockBalances.AsNoTracking()
                .FirstOrDefaultAsync(b =>
                    b.TenantId == tenantId
                    && b.ProductId == productId
                    && b.WarehouseId == warehouseId
                    && b.BatchId == batchId, ct);

            if (bal != null)
                return Result<decimal>.Success(bal.QtyOnHand);

            var batchSum = await _db.StockMovements.AsNoTracking()
                .Where(m => m.TenantId == tenantId
                         && m.ProductId == productId
                         && m.WarehouseId == warehouseId
                         && m.BatchId == batchId)
                .SumAsync(m => (decimal?)m.QtyDelta, ct) ?? 0m;

            return Result<decimal>.Success(batchSum);
        }

        // Physical total across all buckets (null + batch, including expired).
        var fromBalances = await _db.StockBalances.AsNoTracking()
            .Where(b => b.TenantId == tenantId
                     && b.ProductId == productId
                     && b.WarehouseId == warehouseId)
            .SumAsync(b => (decimal?)b.QtyOnHand, ct) ?? 0m;

        if (fromBalances != 0m)
            return Result<decimal>.Success(fromBalances);

        var sum = await _db.StockMovements.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                     && m.ProductId == productId
                     && m.WarehouseId == warehouseId)
            .SumAsync(m => (decimal?)m.QtyDelta, ct) ?? 0m;

        return Result<decimal>.Success(sum);
    }

    public async Task<Result<decimal>> GetAvailableAsync(
        Guid tenantId, Guid productId, Guid warehouseId, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId, ct);
        if (product == null)
            return Result<decimal>.Failure("Product not found / المنتج غير موجود");

        var buckets = await LoadSellableBucketsAsync(tenantId, productId, warehouseId, product, ct);
        return Result<decimal>.Success(buckets.Sum(b => b.Qty));
    }

    public async Task<Result<List<StockAllocationSlice>>> AllocateSaleAsync(
        Guid tenantId, Guid productId, Guid warehouseId, decimal qty, CancellationToken ct = default)
    {
        if (qty <= 0)
            return Result<List<StockAllocationSlice>>.Failure(
                "Quantity must be positive / يجب أن تكون الكمية موجبة");

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId, ct);
        if (product == null)
            return Result<List<StockAllocationSlice>>.Failure("Product not found / المنتج غير موجود");
        if (!product.TrackStock)
            return Result<List<StockAllocationSlice>>.Failure(
                "Product does not track stock / المنتج لا يتتبع المخزون");

        var buckets = await LoadSellableBucketsAsync(tenantId, productId, warehouseId, product, ct);
        var available = buckets.Sum(b => b.Qty);
        if (available < qty)
            return Result<List<StockAllocationSlice>>.Failure(
                $"Insufficient sellable stock (available {available}, requested {qty}) / رصيد قابل للبيع غير كافٍ");

        var remaining = qty;
        var slices = new List<StockAllocationSlice>();
        foreach (var bucket in buckets)
        {
            if (remaining <= 0) break;
            var take = Math.Min(bucket.Qty, remaining);
            if (take <= 0) continue;
            slices.Add(new StockAllocationSlice { BatchId = bucket.BatchId, Qty = take });
            remaining -= take;
        }

        return Result<List<StockAllocationSlice>>.Success(slices);
    }

    public async Task<Result<StockQueryResponse>> QueryStockAsync(
        Guid tenantId, Guid productId, Guid warehouseId, bool includeMovements = false, int movementTake = 50,
        CancellationToken ct = default)
    {
        var productOk = await _db.Products.AnyAsync(p => p.Id == productId && p.TenantId == tenantId, ct);
        if (!productOk)
            return Result<StockQueryResponse>.Failure("Product not found / المنتج غير موجود");

        var whOk = await _db.Warehouses.AnyAsync(w => w.Id == warehouseId && w.TenantId == tenantId, ct);
        if (!whOk)
            return Result<StockQueryResponse>.Failure("Warehouse not found / المخزن غير موجود");

        var onHand = await GetOnHandAsync(tenantId, productId, warehouseId, null, ct);
        if (!onHand.IsSuccess)
            return Result<StockQueryResponse>.Failure(onHand.Error!);

        var available = await GetAvailableAsync(tenantId, productId, warehouseId, ct);
        if (!available.IsSuccess)
            return Result<StockQueryResponse>.Failure(available.Error!);

        var response = new StockQueryResponse
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            QtyOnHand = onHand.Data,
            QtyAvailable = available.Data
        };

        if (includeMovements)
        {
            var take = Math.Clamp(movementTake, 1, 200);
            var rows = await _db.StockMovements.AsNoTracking()
                .Where(m => m.TenantId == tenantId
                         && m.ProductId == productId
                         && m.WarehouseId == warehouseId)
                .OrderByDescending(m => m.OccurredAtUtc)
                .ThenByDescending(m => m.CreatedAtUtc)
                .Take(take)
                .ToListAsync(ct);
            response.Movements = rows.Select(MapMovement).ToList();
        }

        return Result<StockQueryResponse>.Success(response);
    }

    public async Task<Result<ProductStockBreakdownDto>> GetProductStockBreakdownAsync(
        Guid tenantId, Guid productId, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId, ct);
        if (product == null)
            return Result<ProductStockBreakdownDto>.Failure("Product not found / المنتج غير موجود");

        var balances = await _db.StockBalances.AsNoTracking()
            .Include(b => b.Warehouse)
            .Where(b => b.TenantId == tenantId && b.ProductId == productId)
            .ToListAsync(ct);

        var batchIds = balances.Where(b => b.BatchId.HasValue).Select(b => b.BatchId!.Value).Distinct().ToList();
        var batches = batchIds.Count == 0
            ? new Dictionary<Guid, ProductBatch>()
            : await _db.ProductBatches.AsNoTracking()
                .Where(b => b.TenantId == tenantId && batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, ct);

        var today = MembershipOperational.TodayCairo();
        var warehouses = balances
            .GroupBy(b => b.WarehouseId)
            .Select(g =>
            {
                var sample = g.First();
                var physical = g.Sum(x => x.QtyOnHand);
                var sellable = g.Where(x => IsSellableBucket(x.BatchId, batches, product, today))
                    .Sum(x => x.QtyOnHand);
                return new StockOnHandDto
                {
                    ProductId = productId,
                    WarehouseId = g.Key,
                    BatchId = null,
                    QtyOnHand = physical,
                    QtyAvailable = sellable,
                    ProductSku = product.Sku,
                    ProductName = product.Name,
                    WarehouseCode = sample.Warehouse?.Code,
                    WarehouseName = sample.Warehouse?.Name
                };
            })
            .OrderBy(w => w.WarehouseCode)
            .ToList();

        var batchBuckets = balances
            .Where(b => b.QtyOnHand > 0)
            .Select(b =>
            {
                ProductBatch? batch = null;
                if (b.BatchId.HasValue) batches.TryGetValue(b.BatchId.Value, out batch);
                var exp = batch?.ExpiresOn;
                var expired = product.TrackExpiry && exp.HasValue && exp.Value < today;
                return new StockBatchBucketDto
                {
                    WarehouseId = b.WarehouseId,
                    WarehouseCode = b.Warehouse?.Code,
                    BatchId = b.BatchId,
                    BatchNumber = batch?.BatchNumber,
                    ExpiresOn = exp,
                    QtyOnHand = b.QtyOnHand,
                    IsExpired = expired
                };
            })
            .OrderBy(b => b.WarehouseCode)
            .ThenBy(b => b.ExpiresOn)
            .ToList();

        return Result<ProductStockBreakdownDto>.Success(new ProductStockBreakdownDto
        {
            ProductId = productId,
            Sku = product.Sku,
            Name = product.Name,
            TotalOnHand = warehouses.Sum(w => w.QtyOnHand),
            TotalAvailable = warehouses.Sum(w => w.QtyAvailable),
            Warehouses = warehouses,
            Batches = batchBuckets
        });
    }

    public async Task<Result<List<StockBoardRowDto>>> GetStockBoardAsync(
        Guid tenantId, Guid? warehouseId = null, string? q = null, CancellationToken ct = default)
    {
        if (warehouseId.HasValue)
        {
            var whOk = await _db.Warehouses.AnyAsync(
                w => w.Id == warehouseId.Value && w.TenantId == tenantId, ct);
            if (!whOk)
                return Result<List<StockBoardRowDto>>.Failure("Warehouse not found / المخزن غير موجود");
        }

        var productsQ = _db.Products.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.TrackStock && p.IsActive && !p.IsArchived);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            productsQ = productsQ.Where(p =>
                p.Sku.Contains(term) || p.Name.Contains(term)
                || (p.NameAr != null && p.NameAr.Contains(term))
                || (p.Barcode != null && p.Barcode.Contains(term)));
        }

        var products = await productsQ
            .OrderBy(p => p.Name)
            .Take(500)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Name,
                p.NameAr,
                p.ImageUrl,
                p.ReorderMinQty,
                p.TrackExpiry
            })
            .ToListAsync(ct);

        var productIds = products.Select(p => p.Id).ToList();
        var balancesQ = _db.StockBalances.AsNoTracking()
            .Where(b => b.TenantId == tenantId && productIds.Contains(b.ProductId));

        if (warehouseId.HasValue)
            balancesQ = balancesQ.Where(b => b.WarehouseId == warehouseId.Value);

        var balances = await balancesQ
            .Select(b => new { b.ProductId, b.BatchId, b.QtyOnHand })
            .ToListAsync(ct);

        var batchIds = balances.Where(b => b.BatchId.HasValue).Select(b => b.BatchId!.Value).Distinct().ToList();
        var batchExpiry = batchIds.Count == 0
            ? new Dictionary<Guid, DateOnly?>()
            : await _db.ProductBatches.AsNoTracking()
                .Where(b => b.TenantId == tenantId && batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.ExpiresOn, ct);

        var today = MembershipOperational.TodayCairo();
        var trackExpiryByProduct = products.ToDictionary(p => p.Id, p => p.TrackExpiry);

        var onHandByProduct = balances
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QtyOnHand));

        var availableByProduct = balances
            .GroupBy(b => b.ProductId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    trackExpiryByProduct.TryGetValue(g.Key, out var trackExpiry);
                    return g.Where(x =>
                    {
                        if (!x.BatchId.HasValue) return true;
                        if (!trackExpiry) return true;
                        if (!batchExpiry.TryGetValue(x.BatchId.Value, out var exp)) return false;
                        if (!exp.HasValue) return true;
                        return exp.Value >= today;
                    }).Sum(x => x.QtyOnHand);
                });

        string? whCode = null;
        if (warehouseId.HasValue)
        {
            whCode = await _db.Warehouses.AsNoTracking()
                .Where(w => w.Id == warehouseId.Value)
                .Select(w => w.Code)
                .FirstOrDefaultAsync(ct);
        }

        var rows = products.Select(p =>
        {
            onHandByProduct.TryGetValue(p.Id, out var onHand);
            availableByProduct.TryGetValue(p.Id, out var available);
            return new StockBoardRowDto
            {
                ProductId = p.Id,
                Sku = p.Sku,
                Name = p.Name,
                NameAr = p.NameAr,
                ImageUrl = p.ImageUrl,
                ReorderMinQty = p.ReorderMinQty,
                OnHand = onHand,
                Available = available,
                WarehouseId = warehouseId,
                WarehouseCode = whCode
            };
        }).ToList();

        return Result<List<StockBoardRowDto>>.Success(rows);
    }

    private async Task<List<(Guid? BatchId, decimal Qty, DateOnly? ExpiresOn, DateTime BatchCreatedUtc)>> LoadSellableBucketsAsync(
        Guid tenantId, Guid productId, Guid warehouseId, Product product, CancellationToken ct)
    {
        var balances = await _db.StockBalances.AsNoTracking()
            .Where(b => b.TenantId == tenantId
                     && b.ProductId == productId
                     && b.WarehouseId == warehouseId
                     && b.QtyOnHand > 0)
            .ToListAsync(ct);

        var batchIds = balances.Where(b => b.BatchId.HasValue).Select(b => b.BatchId!.Value).Distinct().ToList();
        var batches = batchIds.Count == 0
            ? new Dictionary<Guid, ProductBatch>()
            : await _db.ProductBatches.AsNoTracking()
                .Where(b => b.TenantId == tenantId && batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, ct);

        var today = MembershipOperational.TodayCairo();
        var buckets = new List<(Guid? BatchId, decimal Qty, DateOnly? ExpiresOn, DateTime BatchCreatedUtc)>();

        foreach (var bal in balances)
        {
            if (!IsSellableBucket(bal.BatchId, batches, product, today))
                continue;

            DateOnly? expiresOn = null;
            var created = bal.CreatedAtUtc;
            if (bal.BatchId.HasValue && batches.TryGetValue(bal.BatchId.Value, out var batch))
            {
                expiresOn = batch.ExpiresOn;
                created = batch.CreatedAtUtc;
            }

            buckets.Add((bal.BatchId, bal.QtyOnHand, expiresOn, created));
        }

        // FEFO: earliest expiry first; null expiry after dated; null-batch last.
        return buckets
            .OrderBy(b => b.BatchId.HasValue ? 0 : 1)
            .ThenBy(b => b.ExpiresOn.HasValue ? 0 : 1)
            .ThenBy(b => b.ExpiresOn)
            .ThenBy(b => b.BatchCreatedUtc)
            .ToList();
    }

    private static bool IsSellableBucket(
        Guid? batchId,
        IReadOnlyDictionary<Guid, ProductBatch> batches,
        Product product,
        DateOnly today)
    {
        if (!batchId.HasValue)
            return true;

        if (!batches.TryGetValue(batchId.Value, out var batch))
            return false;

        if (!product.TrackExpiry)
            return true;

        if (!batch.ExpiresOn.HasValue)
            return true;

        return batch.ExpiresOn.Value >= today;
    }

    private async Task<StockBalance> GetOrCreateBalanceAsync(
        Guid tenantId, Guid productId, Guid warehouseId, Guid? batchId, CancellationToken ct)
    {
        var balance = await _db.StockBalances
            .FirstOrDefaultAsync(b =>
                b.TenantId == tenantId
                && b.ProductId == productId
                && b.WarehouseId == warehouseId
                && b.BatchId == batchId, ct);

        if (balance != null)
            return balance;

        balance = new StockBalance
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = warehouseId,
            BatchId = batchId,
            QtyOnHand = 0,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.StockBalances.Add(balance);
        // Flush so concurrent unique index races are visible / SaveChanges together with movement is OK
        return balance;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("UX_stock_movements_Idempotency", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("unique", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
    }

    private static StockMovementDto MapMovement(StockMovement m) => new()
    {
        Id = m.Id,
        ProductId = m.ProductId,
        WarehouseId = m.WarehouseId,
        BatchId = m.BatchId,
        QtyDelta = m.QtyDelta,
        UnitCost = m.UnitCost,
        Reason = m.Reason,
        ReferenceType = m.ReferenceType,
        ReferenceId = m.ReferenceId,
        Note = m.Note,
        OccurredAtUtc = m.OccurredAtUtc,
        CreatedAtUtc = m.CreatedAtUtc
    };
}
