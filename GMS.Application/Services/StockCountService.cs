namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

/// <summary>INVS-9 stock counts — snapshot, submit, approve with drift check via ledger <c>count</c>.</summary>
public class StockCountService : IStockCountService
{
    private readonly GymFlowProDbContext _db;
    private readonly IStockLedgerService _ledger;
    private readonly IAuditService _audit;
    private readonly ILogger<StockCountService> _logger;

    public StockCountService(
        GymFlowProDbContext db,
        IStockLedgerService ledger,
        IAuditService audit,
        ILogger<StockCountService> logger)
    {
        _db = db;
        _ledger = ledger;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<StockCountDto>> CreateAsync(
        Guid tenantId, Guid identityUserId, CreateStockCountRequest request)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockCountDto>.Failure("Staff user not found / المستخدم غير موجود");

        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId && w.TenantId == tenantId);
        if (warehouse == null || !warehouse.IsActive)
            return Result<StockCountDto>.Failure("Warehouse not found or inactive / المخزن غير موجود أو غير نشط");

        List<Product> products;
        if (request.ProductIds != null && request.ProductIds.Count > 0)
        {
            var ids = request.ProductIds.Distinct().ToList();
            products = await _db.Products
                .Where(p => p.TenantId == tenantId && ids.Contains(p.Id))
                .ToListAsync();
            if (products.Count != ids.Count)
                return Result<StockCountDto>.Failure("One or more products not found / منتج واحد أو أكثر غير موجود");
        }
        else
        {
            products = await _db.Products
                .Where(p => p.TenantId == tenantId && p.TrackStock && p.IsActive && !p.IsArchived)
                .OrderBy(p => p.Sku)
                .ToListAsync();
        }

        if (products.Count == 0)
            return Result<StockCountDto>.Failure("No products to count / لا توجد منتجات للجرد");

        var batchTracked = products.FirstOrDefault(p => p.TrackBatch || p.TrackExpiry);
        if (batchTracked != null)
            return Result<StockCountDto>.Failure(
                $"Product {batchTracked.Sku} tracks batches — Counts do not support batch SKUs yet. Use Fix for write-offs / المنتج {batchTracked.Sku} يتتبع التشغيلات — الجرد لا يدعم التشغيلات بعد. استخدم التعديل للشطب");

        foreach (var product in products)
        {
            if (!product.TrackStock || !product.IsActive || product.IsArchived)
                return Result<StockCountDto>.Failure(
                    $"Product {product.Sku} cannot be counted / لا يمكن جرد المنتج {product.Sku}");
        }

        var now = DateTime.UtcNow;
        var entity = new StockCount
        {
            TenantId = tenantId,
            WarehouseId = request.WarehouseId,
            Status = StockCountStatuses.Draft,
            CountedAtUtc = now,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedByUserId = staff.Id,
            CreatedAtUtc = now
        };

        foreach (var product in products)
        {
            var onHand = await _ledger.GetOnHandAsync(tenantId, product.Id, request.WarehouseId);
            if (!onHand.IsSuccess)
                return Result<StockCountDto>.Failure(onHand.Error!);

            var systemQty = onHand.Data;
            entity.Lines.Add(new StockCountLine
            {
                TenantId = tenantId,
                ProductId = product.Id,
                SystemQty = systemQty,
                CountedQty = systemQty,
                Variance = 0,
                CreatedAtUtc = now
            });
        }

        _db.StockCounts.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("stock_count.create", "StockCount", entity.Id, null,
            new { entity.WarehouseId, Lines = entity.Lines.Count });

        return await GetAsync(tenantId, entity.Id);
    }

    public async Task<Result<StockCountDto>> GetAsync(Guid tenantId, Guid id)
    {
        var entity = await LoadAsync(tenantId, id, tracking: false);
        if (entity == null)
            return Result<StockCountDto>.Failure("Stock count not found / الجرد غير موجود");
        return Result<StockCountDto>.Success(Map(entity));
    }

    public async Task<Result<List<StockCountDto>>> ListAsync(Guid tenantId, string? status = null)
    {
        var q = _db.StockCounts.AsNoTracking()
            .Include(c => c.Warehouse)
            .Include(c => c.Lines).ThenInclude(l => l.Product)
            .Where(c => c.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(c => c.Status == status.Trim().ToLowerInvariant());

        var rows = await q.OrderByDescending(c => c.CreatedAtUtc).Take(200).ToListAsync();
        return Result<List<StockCountDto>>.Success(rows.Select(Map).ToList());
    }

    public async Task<Result<StockCountDto>> UpdateLinesAsync(
        Guid tenantId, Guid id, UpdateStockCountLinesRequest request)
    {
        if (request.Lines == null || request.Lines.Count == 0)
            return Result<StockCountDto>.Failure("At least one line update is required / مطلوب تحديث سطر واحد على الأقل");

        var entity = await LoadAsync(tenantId, id, tracking: true);
        if (entity == null)
            return Result<StockCountDto>.Failure("Stock count not found / الجرد غير موجود");

        if (!string.Equals(entity.Status, StockCountStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            return Result<StockCountDto>.Failure(
                $"Cannot update lines in status {entity.Status} / لا يمكن تحديث الأسطر بحالة {entity.Status}");

        var byId = entity.Lines.ToDictionary(l => l.Id);
        foreach (var upd in request.Lines)
        {
            if (!byId.TryGetValue(upd.LineId, out var line))
                return Result<StockCountDto>.Failure("Count line not found / سطر الجرد غير موجود");

            if (upd.CountedQty < 0)
                return Result<StockCountDto>.Failure("CountedQty cannot be negative / الكمية المعدودة لا يمكن أن تكون سالبة");

            var product = line.Product;
            if (product != null && !product.AllowFractionalQty
                && decimal.Truncate(upd.CountedQty) != upd.CountedQty)
                return Result<StockCountDto>.Failure(
                    $"Fractional quantity not allowed for {product.Sku} / الكسور غير مسموحة للمنتج {product.Sku}");

            line.CountedQty = upd.CountedQty;
            line.Variance = upd.CountedQty - line.SystemQty;
            line.UpdatedAtUtc = DateTime.UtcNow;
        }

        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("stock_count.update_lines", "StockCount", entity.Id, null,
            new { Updated = request.Lines.Count });

        return await GetAsync(tenantId, entity.Id);
    }

    public async Task<Result<StockCountDto>> SubmitAsync(
        Guid tenantId, Guid identityUserId, Guid id)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockCountDto>.Failure("Staff user not found / المستخدم غير موجود");

        var entity = await LoadAsync(tenantId, id, tracking: true);
        if (entity == null)
            return Result<StockCountDto>.Failure("Stock count not found / الجرد غير موجود");

        if (!string.Equals(entity.Status, StockCountStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            return Result<StockCountDto>.Failure(
                $"Cannot submit count in status {entity.Status} / لا يمكن تقديم الجرد بحالة {entity.Status}");

        // Recompute variance from stored values
        foreach (var line in entity.Lines)
            line.Variance = line.CountedQty - line.SystemQty;

        entity.Status = StockCountStatuses.Submitted;
        entity.SubmittedByUserId = staff.Id;
        entity.SubmittedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("stock_count.submit", "StockCount", entity.Id, null, new { By = staff.Id });

        return await GetAsync(tenantId, entity.Id);
    }

    public async Task<Result<StockCountDto>> ApproveAsync(
        Guid tenantId, Guid identityUserId, Guid id)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockCountDto>.Failure("Staff user not found / المستخدم غير موجود");

        var entity = await LoadAsync(tenantId, id, tracking: true);
        if (entity == null)
            return Result<StockCountDto>.Failure("Stock count not found / الجرد غير موجود");

        if (!string.Equals(entity.Status, StockCountStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
            return Result<StockCountDto>.Failure(
                $"Cannot approve count in status {entity.Status} / لا يمكن اعتماد الجرد بحالة {entity.Status}");

        var productIds = entity.Lines.Select(l => l.ProductId).Distinct().ToList();
        var batchSku = await _db.Products.AsNoTracking()
            .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id) && (p.TrackBatch || p.TrackExpiry))
            .Select(p => p.Sku)
            .FirstOrDefaultAsync();
        if (batchSku != null)
            return Result<StockCountDto>.Failure(
                $"Product {batchSku} tracks batches — Counts do not support batch SKUs yet. Use Fix / المنتج {batchSku} يتتبع التشغيلات — استخدم التعديل");

        // Drift check: live on-hand must still match frozen SystemQty
        foreach (var line in entity.Lines.OrderBy(l => l.CreatedAtUtc))
        {
            var live = await _ledger.GetOnHandAsync(tenantId, line.ProductId, entity.WarehouseId);
            if (!live.IsSuccess)
                return Result<StockCountDto>.Failure(live.Error!);

            if (live.Data != line.SystemQty)
            {
                var sku = line.Product?.Sku ?? line.ProductId.ToString();
                return Result<StockCountDto>.Failure(
                    $"Stock drifted for {sku}: system snapshot {line.SystemQty}, live {live.Data}. Recount required / تغير المخزون للمنتج {sku}: اللقطة {line.SystemQty}، الحي {live.Data}. مطلوب إعادة الجرد");
            }
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction == null)
            tx = await _db.Database.BeginTransactionAsync();

        try
        {
            foreach (var line in entity.Lines.OrderBy(l => l.CreatedAtUtc))
            {
                line.Variance = line.CountedQty - line.SystemQty;
                if (line.Variance == 0)
                    continue;

                var post = await _ledger.PostAsync(new StockLedgerPostRequest
                {
                    TenantId = tenantId,
                    ProductId = line.ProductId,
                    WarehouseId = entity.WarehouseId,
                    QtyDelta = line.Variance,
                    Reason = StockMovementReasons.Count,
                    ReferenceType = StockReferenceTypes.StockCount,
                    ReferenceId = line.Id,
                    Note = $"Stock count {entity.Id:N}",
                    CreatedByUserId = staff.Id
                });

                if (!post.IsSuccess)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return Result<StockCountDto>.Failure(post.Error!);
                }
            }

            entity.Status = StockCountStatuses.Approved;
            entity.ApprovedByUserId = staff.Id;
            entity.ApprovedAtUtc = DateTime.UtcNow;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (tx != null)
                await tx.CommitAsync();

            await _audit.LogAsync("stock_count.approve", "StockCount", entity.Id, null,
                new { By = staff.Id, Variances = entity.Lines.Count(l => l.Variance != 0) });

            _logger.LogInformation("Approved stock count {Id} with {VarianceLines} variance lines",
                entity.Id, entity.Lines.Count(l => l.Variance != 0));

            return await GetAsync(tenantId, entity.Id);
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Failed approving stock count {Id}", id);
            return Result<StockCountDto>.Failure("Failed to approve stock count / فشل اعتماد الجرد", ex.Message);
        }
        finally
        {
            if (tx != null)
                await tx.DisposeAsync();
        }
    }

    public async Task<Result<StockCountDto>> CancelAsync(
        Guid tenantId, Guid identityUserId, Guid id)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockCountDto>.Failure("Staff user not found / المستخدم غير موجود");

        var entity = await LoadAsync(tenantId, id, tracking: true);
        if (entity == null)
            return Result<StockCountDto>.Failure("Stock count not found / الجرد غير موجود");

        if (!string.Equals(entity.Status, StockCountStatuses.Draft, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entity.Status, StockCountStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
            return Result<StockCountDto>.Failure(
                $"Cannot cancel count in status {entity.Status} / لا يمكن إلغاء الجرد بحالة {entity.Status}");

        entity.Status = StockCountStatuses.Cancelled;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("stock_count.cancel", "StockCount", entity.Id, null, new { By = staff.Id });

        return await GetAsync(tenantId, entity.Id);
    }

    private async Task<StockCount?> LoadAsync(Guid tenantId, Guid id, bool tracking)
    {
        IQueryable<StockCount> q = tracking ? _db.StockCounts : _db.StockCounts.AsNoTracking();
        return await q
            .Include(c => c.Warehouse)
            .Include(c => c.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
    }

    private async Task<AppUser?> ResolveAppUserAsync(Guid tenantId, Guid identityUserId)
    {
        var key = identityUserId.ToString();
        return await _db.AppUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == key);
    }

    private static StockCountDto Map(StockCount c) => new()
    {
        Id = c.Id,
        WarehouseId = c.WarehouseId,
        WarehouseCode = c.Warehouse?.Code,
        Status = c.Status,
        CountedAtUtc = c.CountedAtUtc,
        Note = c.Note,
        CreatedByUserId = c.CreatedByUserId,
        SubmittedAtUtc = c.SubmittedAtUtc,
        ApprovedAtUtc = c.ApprovedAtUtc,
        CreatedAtUtc = c.CreatedAtUtc,
        Lines = c.Lines.Select(l => new StockCountLineDto
        {
            Id = l.Id,
            ProductId = l.ProductId,
            ProductSku = l.Product?.Sku,
            ProductName = l.Product?.Name,
            SystemQty = l.SystemQty,
            CountedQty = l.CountedQty,
            Variance = l.Variance
        }).ToList()
    };
}
