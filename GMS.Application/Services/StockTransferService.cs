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

/// <summary>INVS-8 two-phase warehouse transfers via <see cref="IStockLedgerService"/>.</summary>
public class StockTransferService : IStockTransferService
{
    private readonly GymFlowProDbContext _db;
    private readonly IStockLedgerService _ledger;
    private readonly IAuditService _audit;
    private readonly ILogger<StockTransferService> _logger;

    public StockTransferService(
        GymFlowProDbContext db,
        IStockLedgerService ledger,
        IAuditService audit,
        ILogger<StockTransferService> logger)
    {
        _db = db;
        _ledger = ledger;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<StockTransferDto>> CreatePendingAsync(
        Guid tenantId, Guid identityUserId, CreateStockTransferRequest request)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockTransferDto>.Failure("Staff user not found / المستخدم غير موجود");

        if (request.FromWarehouseId == request.ToWarehouseId)
            return Result<StockTransferDto>.Failure("From and To warehouses must differ / المخزن المصدر والهدف يجب أن يختلفا");

        if (request.Lines == null || request.Lines.Count == 0)
            return Result<StockTransferDto>.Failure("At least one line is required / مطلوب سطر واحد على الأقل");

        var from = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.FromWarehouseId && w.TenantId == tenantId);
        var to = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.ToWarehouseId && w.TenantId == tenantId);
        if (from == null || !from.IsActive || to == null || !to.IsActive)
            return Result<StockTransferDto>.Failure("Warehouse not found or inactive / المخزن غير موجود أو غير نشط");

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        foreach (var line in request.Lines)
        {
            if (line.Qty <= 0)
                return Result<StockTransferDto>.Failure("Qty must be positive / الكمية يجب أن تكون موجبة");

            if (!products.TryGetValue(line.ProductId, out var product))
                return Result<StockTransferDto>.Failure("Product not found / المنتج غير موجود");

            if (!product.TrackStock || !product.IsActive || product.IsArchived)
                return Result<StockTransferDto>.Failure(
                    $"Product {product.Sku} cannot be transferred / لا يمكن نقل المنتج {product.Sku}");

            if (!product.AllowFractionalQty && decimal.Truncate(line.Qty) != line.Qty)
                return Result<StockTransferDto>.Failure(
                    $"Fractional quantity not allowed for {product.Sku} / الكسور غير مسموحة للمنتج {product.Sku}");

            if (line.BatchId.HasValue && !product.TrackBatch && !product.TrackExpiry)
                return Result<StockTransferDto>.Failure(
                    $"Product {product.Sku} does not track batches / المنتج لا يتتبع التشغيلات");
        }

        var entity = new StockTransfer
        {
            TenantId = tenantId,
            FromWarehouseId = request.FromWarehouseId,
            ToWarehouseId = request.ToWarehouseId,
            Status = StockTransferStatuses.Pending,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedByUserId = staff.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var line in request.Lines)
        {
            entity.Lines.Add(new StockTransferLine
            {
                TenantId = tenantId,
                ProductId = line.ProductId,
                Qty = line.Qty,
                BatchId = line.BatchId,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        _db.StockTransfers.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("stock_transfer.create", "StockTransfer", entity.Id, null,
            new { entity.FromWarehouseId, entity.ToWarehouseId, Lines = entity.Lines.Count });

        return await GetAsync(tenantId, entity.Id);
    }

    public async Task<Result<StockTransferDto>> GetAsync(Guid tenantId, Guid id)
    {
        var entity = await LoadAsync(tenantId, id, tracking: false);
        if (entity == null)
            return Result<StockTransferDto>.Failure("Transfer not found / التحويل غير موجود");
        return Result<StockTransferDto>.Success(Map(entity));
    }

    public async Task<Result<InventoryListPageDto<StockTransferDto>>> ListAsync(Guid tenantId, string? status = null)
    {
        var q = _db.StockTransfers.AsNoTracking()
            .Include(t => t.FromWarehouse)
            .Include(t => t.ToWarehouse)
            .Include(t => t.Lines).ThenInclude(l => l.Product)
            .Where(t => t.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(t => t.Status == status.Trim().ToLowerInvariant());

        const int take = 200;
        var rows = await q.OrderByDescending(t => t.CreatedAtUtc).Take(take + 1).ToListAsync();
        var truncated = rows.Count > take;
        if (truncated)
            rows = rows.Take(take).ToList();
        return Result<InventoryListPageDto<StockTransferDto>>.Success(new InventoryListPageDto<StockTransferDto>
        {
            Items = rows.Select(Map).ToList(),
            Truncated = truncated,
            Take = take
        });
    }

    public async Task<Result<StockTransferDto>> SubmitAsync(
        Guid tenantId, Guid identityUserId, Guid id)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockTransferDto>.Failure("Staff user not found / المستخدم غير موجود");

        var entity = await LoadAsync(tenantId, id, tracking: true);
        if (entity == null)
            return Result<StockTransferDto>.Failure("Transfer not found / التحويل غير موجود");

        if (!string.Equals(entity.Status, StockTransferStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            return Result<StockTransferDto>.Failure(
                $"Cannot submit transfer in status {entity.Status} / لا يمكن إرسال التحويل بحالة {entity.Status}");

        return await PostLinesAsync(entity, staff, outbound: true, completeAs: StockTransferStatuses.InTransit,
            setSubmitted: true, auditAction: "stock_transfer.submit", allocateFefo: true,
            claimFromStatus: StockTransferStatuses.Pending);
    }

    /// <summary>
    /// G2 Policy A — resolve outbound slices. Explicit BatchId must be sellable; null BatchId uses
    /// <see cref="IStockLedgerService.AllocateSaleAsync"/> (same FEFO as retail sale). Lines are not mutated.
    /// </summary>
    private async Task<Result<List<(StockTransferLine Line, Guid? BatchId, decimal Qty)>>> ResolveOutboundSlicesAsync(
        StockTransfer entity)
    {
        var productIds = entity.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => p.TenantId == entity.TenantId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var today = MembershipOperational.TodayCairo();
        var slices = new List<(StockTransferLine Line, Guid? BatchId, decimal Qty)>();

        foreach (var line in entity.Lines.OrderBy(l => l.CreatedAtUtc))
        {
            if (!products.TryGetValue(line.ProductId, out var product))
                return Result<List<(StockTransferLine, Guid?, decimal)>>.Failure(
                    "Product not found / المنتج غير موجود");

            if (line.BatchId.HasValue)
            {
                var batch = await _db.ProductBatches.AsNoTracking()
                    .FirstOrDefaultAsync(b =>
                        b.Id == line.BatchId.Value
                        && b.TenantId == entity.TenantId
                        && b.ProductId == line.ProductId);
                if (batch == null)
                    return Result<List<(StockTransferLine, Guid?, decimal)>>.Failure(
                        $"Batch not found for {product.Sku} / التشغيلة غير موجودة للمنتج {product.Sku}");

                if (product.TrackExpiry && batch.ExpiresOn.HasValue && batch.ExpiresOn.Value < today)
                    return Result<List<(StockTransferLine, Guid?, decimal)>>.Failure(
                        $"Cannot transfer expired batch for {product.Sku} / لا يمكن نقل تشغيلة منتهية للمنتج {product.Sku}");

                var onHand = await _ledger.GetOnHandAsync(
                    entity.TenantId, line.ProductId, entity.FromWarehouseId, line.BatchId);
                if (!onHand.IsSuccess)
                    return Result<List<(StockTransferLine, Guid?, decimal)>>.Failure(onHand.Error!);
                if (onHand.Data < line.Qty)
                    return Result<List<(StockTransferLine, Guid?, decimal)>>.Failure(
                        $"Insufficient stock at source for {product.Sku}: on hand {onHand.Data}, need {line.Qty} / رصيد غير كافٍ في المصدر");

                slices.Add((line, line.BatchId, line.Qty));
                continue;
            }

            var alloc = await _ledger.AllocateSaleAsync(
                entity.TenantId, line.ProductId, entity.FromWarehouseId, line.Qty);
            if (!alloc.IsSuccess)
            {
                var available = await _ledger.GetAvailableAsync(
                    entity.TenantId, line.ProductId, entity.FromWarehouseId);
                var availText = available.IsSuccess ? available.Data.ToString() : "?";
                return Result<List<(StockTransferLine, Guid?, decimal)>>.Failure(
                    $"Insufficient sellable stock at source for {product.Sku}: available {availText}, need {line.Qty} / رصيد قابل للبيع غير كافٍ في المصدر");
            }

            foreach (var slice in alloc.Data!)
                slices.Add((line, slice.BatchId, slice.Qty));
        }

        return Result<List<(StockTransferLine, Guid?, decimal)>>.Success(slices);
    }

    public async Task<Result<StockTransferDto>> ReceiveAsync(
        Guid tenantId, Guid identityUserId, Guid id)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockTransferDto>.Failure("Staff user not found / المستخدم غير موجود");

        var entity = await LoadAsync(tenantId, id, tracking: true);
        if (entity == null)
            return Result<StockTransferDto>.Failure("Transfer not found / التحويل غير موجود");

        if (!string.Equals(entity.Status, StockTransferStatuses.InTransit, StringComparison.OrdinalIgnoreCase))
            return Result<StockTransferDto>.Failure(
                $"Cannot receive transfer in status {entity.Status} / لا يمكن استلام التحويل بحالة {entity.Status}");

        return await PostLinesAsync(entity, staff, outbound: false, completeAs: StockTransferStatuses.Completed,
            setReceived: true, auditAction: "stock_transfer.receive",
            claimFromStatus: StockTransferStatuses.InTransit);
    }

    public async Task<Result<StockTransferDto>> CancelAsync(
        Guid tenantId, Guid identityUserId, Guid id)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockTransferDto>.Failure("Staff user not found / المستخدم غير موجود");

        var entity = await LoadAsync(tenantId, id, tracking: true);
        if (entity == null)
            return Result<StockTransferDto>.Failure("Transfer not found / التحويل غير موجود");

        if (!string.Equals(entity.Status, StockTransferStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            return Result<StockTransferDto>.Failure(
                "Only pending transfers can be cancelled — use reject for in-transit / يمكن إلغاء المسودات فقط — استخدم الرفض للنقل الجاري");

        entity.Status = StockTransferStatuses.Cancelled;
        entity.CancelledByUserId = staff.Id;
        entity.CancelledAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("stock_transfer.cancel", "StockTransfer", entity.Id, null, new { By = staff.Id });

        return await GetAsync(tenantId, entity.Id);
    }

    public async Task<Result<StockTransferDto>> RejectAsync(
        Guid tenantId, Guid identityUserId, Guid id)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<StockTransferDto>.Failure("Staff user not found / المستخدم غير موجود");

        var entity = await LoadAsync(tenantId, id, tracking: true);
        if (entity == null)
            return Result<StockTransferDto>.Failure("Transfer not found / التحويل غير موجود");

        if (!string.Equals(entity.Status, StockTransferStatuses.InTransit, StringComparison.OrdinalIgnoreCase))
            return Result<StockTransferDto>.Failure(
                $"Cannot reject transfer in status {entity.Status} / لا يمكن رفض التحويل بحالة {entity.Status}");

        // Compensating transfer_in back to source — mirror outbound TransferOut batches.
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction == null)
            tx = await _db.Database.BeginTransactionAsync();

        try
        {
            var claimed = await TryClaimTransferStatusAsync(
                tenantId, entity.Id, StockTransferStatuses.InTransit, StockTransferStatuses.Cancelled, entity);
            if (!claimed)
            {
                if (tx != null) await tx.RollbackAsync();
                return Result<StockTransferDto>.Failure(
                    "Transfer already received or rejected / التحويل تم استلامه أو رفضه بالفعل");
            }

            entity.Status = StockTransferStatuses.Cancelled;
            entity.CancelledByUserId = staff.Id;
            entity.CancelledAtUtc = DateTime.UtcNow;
            entity.UpdatedAtUtc = DateTime.UtcNow;

            var productIds = entity.Lines.Select(l => l.ProductId).Distinct().ToList();
            var costByProduct = await _db.Products.AsNoTracking()
                .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.CostPrice);

            foreach (var line in entity.Lines.OrderBy(l => l.CreatedAtUtc))
            {
                costByProduct.TryGetValue(line.ProductId, out var unitCost);
                var outbound = await LoadOutboundSlicesAsync(entity.TenantId, line.Id);
                if (outbound.Count == 0)
                {
                    // Legacy single-post fallback (pre-G2).
                    outbound.Add((line.BatchId, line.Qty));
                }

                foreach (var (batchId, qty) in outbound)
                {
                    var post = await _ledger.PostAsync(new StockLedgerPostRequest
                    {
                        TenantId = tenantId,
                        ProductId = line.ProductId,
                        WarehouseId = entity.FromWarehouseId,
                        BatchId = batchId,
                        QtyDelta = qty,
                        UnitCost = unitCost,
                        Reason = StockMovementReasons.TransferIn,
                        ReferenceType = StockReferenceTypes.StockTransferRejectLine,
                        ReferenceId = line.Id,
                        Note = $"Transfer reject {entity.Id:N}",
                        CreatedByUserId = staff.Id
                    });

                    if (!post.IsSuccess)
                    {
                        if (tx != null) await tx.RollbackAsync();
                        return Result<StockTransferDto>.Failure(post.Error!);
                    }
                }
            }

            await _db.SaveChangesAsync();

            if (tx != null)
                await tx.CommitAsync();

            await _audit.LogAsync("stock_transfer.reject", "StockTransfer", entity.Id, null, new { By = staff.Id });
            _logger.LogInformation("Rejected transfer {Id} — stock returned to source", entity.Id);

            return await GetAsync(tenantId, entity.Id);
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Failed rejecting transfer {Id}", id);
            return Result<StockTransferDto>.Failure(
                $"Failed to reject transfer / فشل رفض التحويل: {ex.Message}", ex.Message);
        }
        finally
        {
            if (tx != null)
                await tx.DisposeAsync();
        }
    }

    private async Task<bool> TryClaimTransferStatusAsync(
        Guid tenantId, Guid transferId, string fromStatus, string toStatus, StockTransfer? tracked = null)
    {
        if (!_db.Database.IsRelational())
        {
            var row = tracked
                ?? await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == transferId && t.TenantId == tenantId);
            if (row == null || !string.Equals(row.Status, fromStatus, StringComparison.OrdinalIgnoreCase))
                return false;
            row.Status = toStatus;
            row.UpdatedAtUtc = DateTime.UtcNow;
            return true;
        }

        var now = DateTime.UtcNow;
        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE stock_transfers
SET Status = {toStatus}, UpdatedAtUtc = {now}
WHERE Id = {transferId} AND TenantId = {tenantId}
  AND Status = {fromStatus} AND IsDeleted = 0");
        return rows == 1;
    }

    private async Task<List<(Guid? BatchId, decimal Qty)>> LoadOutboundSlicesAsync(Guid tenantId, Guid lineId)
    {
        var rows = await _db.StockMovements.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                        && m.ReferenceType == StockReferenceTypes.StockTransferLine
                        && m.ReferenceId == lineId
                        && m.Reason == StockMovementReasons.TransferOut)
            .Select(m => new { m.BatchId, m.QtyDelta })
            .ToListAsync();

        return rows.Select(m => (m.BatchId, Math.Abs(m.QtyDelta))).ToList();
    }

    private async Task<Result<StockTransferDto>> PostLinesAsync(
        StockTransfer entity,
        AppUser staff,
        bool outbound,
        string completeAs,
        string auditAction,
        bool setSubmitted = false,
        bool setReceived = false,
        bool allocateFefo = false,
        string? claimFromStatus = null)
    {
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction == null)
            tx = await _db.Database.BeginTransactionAsync();

        try
        {
            if (!string.IsNullOrEmpty(claimFromStatus))
            {
                var claimed = await TryClaimTransferStatusAsync(
                    entity.TenantId, entity.Id, claimFromStatus, completeAs, entity);
                if (!claimed)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return Result<StockTransferDto>.Failure(
                        "Transfer status changed by another user — retry / حالة التحويل تغيرت — أعد المحاولة");
                }

                entity.Status = completeAs;
                entity.UpdatedAtUtc = DateTime.UtcNow;
            }

            var productIds = entity.Lines.Select(l => l.ProductId).Distinct().ToList();
            var costByProduct = await _db.Products.AsNoTracking()
                .Where(p => p.TenantId == entity.TenantId && productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.CostPrice);

            if (outbound)
            {
                List<(StockTransferLine Line, Guid? BatchId, decimal Qty)> posts;
                if (allocateFefo)
                {
                    var resolved = await ResolveOutboundSlicesAsync(entity);
                    if (!resolved.IsSuccess)
                    {
                        if (tx != null) await tx.RollbackAsync();
                        return Result<StockTransferDto>.Failure(resolved.Error!);
                    }
                    posts = resolved.Data!;
                }
                else
                {
                    posts = entity.Lines
                        .OrderBy(l => l.CreatedAtUtc)
                        .Select(l => (l, l.BatchId, l.Qty))
                        .ToList();
                }

                foreach (var (line, batchId, qty) in posts)
                {
                    costByProduct.TryGetValue(line.ProductId, out var unitCost);
                    var post = await _ledger.PostAsync(new StockLedgerPostRequest
                    {
                        TenantId = entity.TenantId,
                        ProductId = line.ProductId,
                        WarehouseId = entity.FromWarehouseId,
                        BatchId = batchId,
                        QtyDelta = -qty,
                        UnitCost = unitCost,
                        Reason = StockMovementReasons.TransferOut,
                        ReferenceType = StockReferenceTypes.StockTransferLine,
                        ReferenceId = line.Id,
                        Note = $"Transfer {entity.Id:N}",
                        CreatedByUserId = staff.Id
                    });

                    if (!post.IsSuccess)
                    {
                        if (tx != null) await tx.RollbackAsync();
                        return Result<StockTransferDto>.Failure(post.Error!);
                    }
                }
            }
            else
            {
                // Receive: mirror each TransferOut batch into destination (Sale-style multi-post).
                foreach (var line in entity.Lines.OrderBy(l => l.CreatedAtUtc))
                {
                    costByProduct.TryGetValue(line.ProductId, out var unitCost);
                    var outboundSlices = await LoadOutboundSlicesAsync(entity.TenantId, line.Id);
                    if (outboundSlices.Count == 0)
                        outboundSlices.Add((line.BatchId, line.Qty));

                    foreach (var (batchId, qty) in outboundSlices)
                    {
                        var post = await _ledger.PostAsync(new StockLedgerPostRequest
                        {
                            TenantId = entity.TenantId,
                            ProductId = line.ProductId,
                            WarehouseId = entity.ToWarehouseId,
                            BatchId = batchId,
                            QtyDelta = qty,
                            UnitCost = unitCost,
                            Reason = StockMovementReasons.TransferIn,
                            ReferenceType = StockReferenceTypes.StockTransferLine,
                            ReferenceId = line.Id,
                            Note = $"Transfer {entity.Id:N}",
                            CreatedByUserId = staff.Id
                        });

                        if (!post.IsSuccess)
                        {
                            if (tx != null) await tx.RollbackAsync();
                            return Result<StockTransferDto>.Failure(post.Error!);
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(claimFromStatus))
                entity.Status = completeAs;

            entity.UpdatedAtUtc = DateTime.UtcNow;
            if (setSubmitted)
            {
                entity.SubmittedByUserId = staff.Id;
                entity.SubmittedAtUtc = DateTime.UtcNow;
            }
            if (setReceived)
            {
                entity.ReceivedByUserId = staff.Id;
                entity.ReceivedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            if (tx != null)
                await tx.CommitAsync();

            await _audit.LogAsync(auditAction, "StockTransfer", entity.Id, null,
                new { entity.Status, Lines = entity.Lines.Count });

            return await GetAsync(entity.TenantId, entity.Id);
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Failed transfer action {Action} for {Id}", auditAction, entity.Id);
            return Result<StockTransferDto>.Failure(
                $"Failed to process transfer / فشل معالجة التحويل: {ex.Message}", ex.Message);
        }
        finally
        {
            if (tx != null)
                await tx.DisposeAsync();
        }
    }

    private async Task<StockTransfer?> LoadAsync(Guid tenantId, Guid id, bool tracking)
    {
        IQueryable<StockTransfer> q = tracking ? _db.StockTransfers : _db.StockTransfers.AsNoTracking();
        return await q
            .Include(t => t.FromWarehouse)
            .Include(t => t.ToWarehouse)
            .Include(t => t.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId);
    }

    private async Task<AppUser?> ResolveAppUserAsync(Guid tenantId, Guid identityUserId)
    {
        var key = identityUserId.ToString();
        return await _db.AppUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == key);
    }

    private static StockTransferDto Map(StockTransfer t) => new()
    {
        Id = t.Id,
        FromWarehouseId = t.FromWarehouseId,
        FromWarehouseCode = t.FromWarehouse?.Code,
        ToWarehouseId = t.ToWarehouseId,
        ToWarehouseCode = t.ToWarehouse?.Code,
        Status = t.Status,
        Note = t.Note,
        CreatedByUserId = t.CreatedByUserId,
        SubmittedAtUtc = t.SubmittedAtUtc,
        ReceivedAtUtc = t.ReceivedAtUtc,
        CancelledAtUtc = t.CancelledAtUtc,
        CreatedAtUtc = t.CreatedAtUtc,
        Lines = t.Lines.Select(l => new StockTransferLineDto
        {
            Id = l.Id,
            ProductId = l.ProductId,
            ProductSku = l.Product?.Sku,
            ProductName = l.Product?.Name,
            Qty = l.Qty,
            BatchId = l.BatchId
        }).ToList()
    };
}
