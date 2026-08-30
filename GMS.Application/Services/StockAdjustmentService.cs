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

/// <summary>INVS-4 + G4 Fix truth — structured reasons, batch integrity, cost trail via ledger.</summary>
public class StockAdjustmentService : IStockAdjustmentService
{
    private readonly GymFlowProDbContext _db;
    private readonly IStockLedgerService _ledger;
    private readonly IWarehouseService _warehouses;
    private readonly IAuditService _audit;
    private readonly ILogger<StockAdjustmentService> _logger;

    public StockAdjustmentService(
        GymFlowProDbContext db,
        IStockLedgerService ledger,
        IWarehouseService warehouses,
        IAuditService audit,
        ILogger<StockAdjustmentService> logger)
    {
        _db = db;
        _ledger = ledger;
        _warehouses = warehouses;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<StockAdjustmentDto>> CreateDraftAsync(
        Guid tenantId, Guid identityUserId, CreateStockAdjustmentRequest request)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockAdjustmentDto>.Failure("Staff user not found / المستخدم غير موجود");

        if (string.IsNullOrWhiteSpace(request.ReasonCode)
            || !StockAdjustmentReasonCodes.All.Contains(request.ReasonCode.Trim()))
            return Result<StockAdjustmentDto>.Failure("Invalid reason code / سبب غير صالح");

        var reasonCode = request.ReasonCode.Trim().ToLowerInvariant();

        if (StockAdjustmentReasonCodes.RequiresNote(reasonCode)
            && string.IsNullOrWhiteSpace(request.Note))
            return Result<StockAdjustmentDto>.Failure(
                "Note is required for reason 'other' / الملاحظة مطلوبة لسبب «أخرى»");

        if (request.Lines == null || request.Lines.Count == 0)
            return Result<StockAdjustmentDto>.Failure("At least one line is required / مطلوب سطر واحد على الأقل");

        Warehouse? warehouse;
        if (request.WarehouseId.HasValue && request.WarehouseId.Value != Guid.Empty)
        {
            warehouse = await _db.Warehouses
                .FirstOrDefaultAsync(w => w.Id == request.WarehouseId.Value && w.TenantId == tenantId);
            if (warehouse == null || !warehouse.IsActive)
                return Result<StockAdjustmentDto>.Failure("Warehouse not found or inactive / المخزن غير موجود أو غير نشط");
        }
        else
        {
            var resolved = await _warehouses.GetOrCreateDefaultAsync(tenantId);
            if (!resolved.IsSuccess || resolved.Data == null)
                return Result<StockAdjustmentDto>.Failure("No usable warehouse for this tenant / لا يوجد مخزن متاح لهذا المستأجر");
            warehouse = resolved.Data;
        }

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var today = MembershipOperational.TodayCairo();
        var batchIds = request.Lines.Where(l => l.BatchId.HasValue).Select(l => l.BatchId!.Value).Distinct().ToList();
        var batches = batchIds.Count == 0
            ? new Dictionary<Guid, ProductBatch>()
            : await _db.ProductBatches
                .Where(b => b.TenantId == tenantId && batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id);

        foreach (var line in request.Lines)
        {
            if (line.QtyDelta == 0)
                return Result<StockAdjustmentDto>.Failure("Line QtyDelta cannot be zero / كمية السطر لا يمكن أن تكون صفر");

            if (StockAdjustmentReasonCodes.RequiresIncrease(reasonCode) && line.QtyDelta < 0)
                return Result<StockAdjustmentDto>.Failure(
                    "Opening stock lines must be positive / كميات المخزون الافتتاحي يجب أن تكون موجبة");

            if (StockAdjustmentReasonCodes.RequiresDecrease(reasonCode) && line.QtyDelta > 0)
                return Result<StockAdjustmentDto>.Failure(
                    $"Reason '{reasonCode}' requires a negative qty (write-off) / السبب يتطلب كمية سالبة");

            if (!products.TryGetValue(line.ProductId, out var product))
                return Result<StockAdjustmentDto>.Failure("Product not found / المنتج غير موجود");

            if (!product.TrackStock || !product.IsActive || product.IsArchived)
                return Result<StockAdjustmentDto>.Failure(
                    $"Product {product.Sku} cannot be adjusted / لا يمكن تعديل المنتج {product.Sku}");

            if (!product.AllowFractionalQty && decimal.Truncate(line.QtyDelta) != line.QtyDelta)
                return Result<StockAdjustmentDto>.Failure(
                    $"Fractional quantity not allowed for {product.Sku} / الكسور غير مسموحة للمنتج {product.Sku}");

            var needsBatch =
                StockAdjustmentReasonCodes.RequiresBatch(reasonCode)
                || ((product.TrackBatch || product.TrackExpiry) && line.QtyDelta < 0);

            if (needsBatch && !line.BatchId.HasValue)
                return Result<StockAdjustmentDto>.Failure(
                    $"Batch is required for {product.Sku} on this adjustment / التشغيلة مطلوبة للمنتج {product.Sku}");

            if (line.BatchId.HasValue)
            {
                if (!product.TrackBatch && !product.TrackExpiry)
                    return Result<StockAdjustmentDto>.Failure(
                        $"Product {product.Sku} does not track batches / المنتج لا يتتبع التشغيلات");

                if (!batches.TryGetValue(line.BatchId.Value, out var batch)
                    || batch.ProductId != line.ProductId)
                    return Result<StockAdjustmentDto>.Failure(
                        $"Batch not found for {product.Sku} / التشغيلة غير موجودة للمنتج {product.Sku}");

                if (reasonCode == StockAdjustmentReasonCodes.Expired)
                {
                    if (!product.TrackExpiry)
                        return Result<StockAdjustmentDto>.Failure(
                            $"Product {product.Sku} does not track expiry / المنتج لا يتتبع الصلاحية");
                    if (!batch.ExpiresOn.HasValue || batch.ExpiresOn.Value >= today)
                        return Result<StockAdjustmentDto>.Failure(
                            $"Batch for {product.Sku} is not expired / التشغيلة للمنتج {product.Sku} ليست منتهية");
                }
            }
        }

        var entity = new StockAdjustment
        {
            TenantId = tenantId,
            WarehouseId = warehouse!.Id,
            Status = StockAdjustmentStatuses.Draft,
            ReasonCode = reasonCode,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedByUserId = staff.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var line in request.Lines)
        {
            var product = products[line.ProductId];
            entity.Lines.Add(new StockAdjustmentLine
            {
                TenantId = tenantId,
                ProductId = line.ProductId,
                QtyDelta = line.QtyDelta,
                UnitCost = line.UnitCost ?? product.CostPrice,
                BatchId = line.BatchId,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        _db.StockAdjustments.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("stock_adjustment.create", "StockAdjustment", entity.Id, null,
            new
            {
                entity.ReasonCode,
                entity.WarehouseId,
                Lines = entity.Lines.Count,
                EstimatedValueImpactEgp = entity.Lines.Sum(l => l.QtyDelta * (l.UnitCost ?? 0m)),
                By = staff.Id
            });

        return await GetAsync(tenantId, entity.Id);
    }

    public async Task<Result<StockAdjustmentDto>> GetAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.StockAdjustments.AsNoTracking()
            .Include(a => a.Warehouse)
            .Include(a => a.Lines).ThenInclude(l => l.Product)
            .Include(a => a.Lines).ThenInclude(l => l.Batch)
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId);

        if (entity == null)
            return Result<StockAdjustmentDto>.Failure("Adjustment not found / التسوية غير موجودة");

        return Result<StockAdjustmentDto>.Success(Map(entity));
    }

    public async Task<Result<List<StockAdjustmentDto>>> ListAsync(
        Guid tenantId, string? status = null, int take = 50)
    {
        take = take <= 0 ? 50 : Math.Min(take, 200);

        var q = _db.StockAdjustments.AsNoTracking()
            .Include(a => a.Warehouse)
            .Include(a => a.Lines)
            .Where(a => a.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(a => a.Status == status.Trim().ToLowerInvariant());

        var rows = await q.OrderByDescending(a => a.CreatedAtUtc).Take(take).ToListAsync();
        return Result<List<StockAdjustmentDto>>.Success(rows.Select(MapListItem).ToList());
    }

    public async Task<Result<StockAdjustmentDto>> PostAsync(
        Guid tenantId, Guid identityUserId, Guid adjustmentId)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockAdjustmentDto>.Failure("Staff user not found / المستخدم غير موجود");

        var entity = await _db.StockAdjustments
            .Include(a => a.Lines)
            .FirstOrDefaultAsync(a => a.Id == adjustmentId && a.TenantId == tenantId);

        if (entity == null)
            return Result<StockAdjustmentDto>.Failure("Adjustment not found / التسوية غير موجودة");

        if (!string.Equals(entity.Status, StockAdjustmentStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            return Result<StockAdjustmentDto>.Failure(
                $"Cannot post adjustment in status {entity.Status} / لا يمكن ترحيل التسوية بحالة {entity.Status}");

        if (entity.Lines.Count == 0)
            return Result<StockAdjustmentDto>.Failure("Adjustment has no lines / التسوية بلا أسطر");

        var ledgerReason = string.Equals(entity.ReasonCode, StockAdjustmentReasonCodes.Opening, StringComparison.OrdinalIgnoreCase)
            ? StockMovementReasons.Opening
            : StockMovementReasons.Adjustment;

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction == null)
            tx = await _db.Database.BeginTransactionAsync();

        try
        {
            foreach (var line in entity.Lines.OrderBy(l => l.CreatedAtUtc))
            {
                var note = string.IsNullOrWhiteSpace(entity.Note)
                    ? entity.ReasonCode
                    : $"{entity.ReasonCode}: {entity.Note}";

                var post = await _ledger.PostAsync(new StockLedgerPostRequest
                {
                    TenantId = tenantId,
                    ProductId = line.ProductId,
                    WarehouseId = entity.WarehouseId,
                    BatchId = line.BatchId,
                    QtyDelta = line.QtyDelta,
                    UnitCost = line.UnitCost,
                    Reason = ledgerReason,
                    ReferenceType = StockReferenceTypes.StockAdjustment,
                    ReferenceId = line.Id,
                    Note = note,
                    CreatedByUserId = staff.Id
                });

                if (!post.IsSuccess)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return Result<StockAdjustmentDto>.Failure(post.Error!);
                }
            }

            entity.Status = StockAdjustmentStatuses.Posted;
            entity.PostedByUserId = staff.Id;
            entity.PostedAtUtc = DateTime.UtcNow;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (tx != null)
                await tx.CommitAsync();

            var impact = entity.Lines.Sum(l => l.QtyDelta * (l.UnitCost ?? 0m));
            await _audit.LogAsync("stock_adjustment.post", "StockAdjustment", entity.Id, null,
                new
                {
                    entity.ReasonCode,
                    Lines = entity.Lines.Count,
                    EstimatedValueImpactEgp = impact,
                    By = staff.Id,
                    Batches = entity.Lines.Count(l => l.BatchId.HasValue)
                });

            _logger.LogInformation("Posted stock adjustment {Id} reason {Reason} lines {Count} impact {Impact}",
                entity.Id, entity.ReasonCode, entity.Lines.Count, impact);

            return await GetAsync(tenantId, entity.Id);
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Failed posting stock adjustment {Id}", adjustmentId);
            return Result<StockAdjustmentDto>.Failure(
                $"Failed to post adjustment / فشل ترحيل التسوية: {ex.Message}", ex.Message);
        }
        finally
        {
            if (tx != null)
                await tx.DisposeAsync();
        }
    }

    public async Task<Result<StockAdjustmentDto>> CancelAsync(
        Guid tenantId, Guid identityUserId, Guid adjustmentId)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockAdjustmentDto>.Failure("Staff user not found / المستخدم غير موجود");

        var entity = await _db.StockAdjustments
            .FirstOrDefaultAsync(a => a.Id == adjustmentId && a.TenantId == tenantId);

        if (entity == null)
            return Result<StockAdjustmentDto>.Failure("Adjustment not found / التسوية غير موجودة");

        if (!string.Equals(entity.Status, StockAdjustmentStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            return Result<StockAdjustmentDto>.Failure(
                "Only draft adjustments can be cancelled / يمكن إلغاء المسودات فقط");

        entity.Status = StockAdjustmentStatuses.Cancelled;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("stock_adjustment.cancel", "StockAdjustment", entity.Id, null,
            new { By = staff.Id });

        return await GetAsync(tenantId, entity.Id);
    }

    private async Task<AppUser?> ResolveAppUserAsync(Guid tenantId, Guid identityUserId)
    {
        var key = identityUserId.ToString();
        return await _db.AppUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == key);
    }

    private static decimal Impact(IEnumerable<StockAdjustmentLine> lines) =>
        lines.Sum(l => l.QtyDelta * (l.UnitCost ?? 0m));

    private static StockAdjustmentDto Map(StockAdjustment a) => new()
    {
        Id = a.Id,
        WarehouseId = a.WarehouseId,
        WarehouseCode = a.Warehouse?.Code,
        WarehouseName = a.Warehouse?.Name,
        Status = a.Status,
        ReasonCode = a.ReasonCode,
        Note = a.Note,
        CreatedByUserId = a.CreatedByUserId,
        PostedByUserId = a.PostedByUserId,
        PostedAtUtc = a.PostedAtUtc,
        CreatedAtUtc = a.CreatedAtUtc,
        LineCount = a.Lines?.Count ?? 0,
        EstimatedValueImpactEgp = Impact(a.Lines ?? Enumerable.Empty<StockAdjustmentLine>()),
        Lines = (a.Lines ?? new List<StockAdjustmentLine>()).Select(l => new StockAdjustmentLineDto
        {
            Id = l.Id,
            ProductId = l.ProductId,
            ProductSku = l.Product?.Sku,
            ProductName = l.Product?.Name,
            QtyDelta = l.QtyDelta,
            UnitCost = l.UnitCost,
            BatchId = l.BatchId,
            BatchNumber = l.Batch?.BatchNumber,
            ExpiresOn = l.Batch?.ExpiresOn
        }).ToList()
    };

    private static StockAdjustmentDto MapListItem(StockAdjustment a) => new()
    {
        Id = a.Id,
        WarehouseId = a.WarehouseId,
        WarehouseCode = a.Warehouse?.Code,
        WarehouseName = a.Warehouse?.Name,
        Status = a.Status,
        ReasonCode = a.ReasonCode,
        Note = a.Note,
        CreatedByUserId = a.CreatedByUserId,
        PostedByUserId = a.PostedByUserId,
        PostedAtUtc = a.PostedAtUtc,
        CreatedAtUtc = a.CreatedAtUtc,
        LineCount = a.Lines?.Count ?? 0,
        EstimatedValueImpactEgp = Impact(a.Lines ?? Enumerable.Empty<StockAdjustmentLine>()),
        Lines = new List<StockAdjustmentLineDto>()
    };
}
