namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

/// <summary>INVS-5 purchase orders &amp; goods receipts — stock via <see cref="IStockLedgerService"/>.</summary>
public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly GymFlowProDbContext _db;
    private readonly IStockLedgerService _ledger;
    private readonly IInventoryReorderCalculator _reorder;
    private readonly IAuditService _audit;
    private readonly ILogger<PurchaseOrderService> _logger;

    public PurchaseOrderService(
        GymFlowProDbContext db,
        IStockLedgerService ledger,
        IInventoryReorderCalculator reorder,
        IAuditService audit,
        ILogger<PurchaseOrderService> logger)
    {
        _db = db;
        _ledger = ledger;
        _reorder = reorder;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<PurchaseOrderDto>> CreateDraftAsync(
        Guid tenantId, CreatePurchaseOrderRequest request)
    {
        if (request.Lines == null || request.Lines.Count == 0)
            return Result<PurchaseOrderDto>.Failure("At least one line is required / مطلوب سطر واحد على الأقل");

        var supplier = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId && s.TenantId == tenantId);
        if (supplier == null || !supplier.IsActive)
            return Result<PurchaseOrderDto>.Failure("Supplier not found or inactive / المورد غير موجود أو غير نشط");

        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId && w.TenantId == tenantId);
        if (warehouse == null || !warehouse.IsActive)
            return Result<PurchaseOrderDto>.Failure("Warehouse not found or inactive / المخزن غير موجود أو غير نشط");

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        foreach (var line in request.Lines)
        {
            if (line.QtyOrdered <= 0)
                return Result<PurchaseOrderDto>.Failure("QtyOrdered must be positive / الكمية المطلوبة يجب أن تكون موجبة");
            if (line.UnitCost < 0)
                return Result<PurchaseOrderDto>.Failure("UnitCost cannot be negative / التكلفة لا يمكن أن تكون سالبة");
            if (!products.TryGetValue(line.ProductId, out var product))
                return Result<PurchaseOrderDto>.Failure("Product not found / المنتج غير موجود");
            if (!product.TrackStock || !product.IsActive || product.IsArchived)
                return Result<PurchaseOrderDto>.Failure(
                    $"Product {product.Sku} cannot be purchased into stock / لا يمكن شراء المنتج {product.Sku}");
            if (!product.AllowFractionalQty && decimal.Truncate(line.QtyOrdered) != line.QtyOrdered)
                return Result<PurchaseOrderDto>.Failure(
                    $"Fractional quantity not allowed for {product.Sku} / الكسور غير مسموحة للمنتج {product.Sku}");
        }

        var po = new PurchaseOrder
        {
            TenantId = tenantId,
            SupplierId = request.SupplierId,
            WarehouseId = request.WarehouseId,
            Status = PurchaseOrderStatuses.Draft,
            OrderedAtUtc = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var line in request.Lines)
        {
            po.Lines.Add(new PurchaseOrderLine
            {
                TenantId = tenantId,
                ProductId = line.ProductId,
                QtyOrdered = line.QtyOrdered,
                QtyReceived = 0,
                UnitCost = line.UnitCost,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("purchase_order.create", "PurchaseOrder", po.Id, null,
            new { po.SupplierId, po.WarehouseId, Lines = po.Lines.Count });

        return await GetAsync(tenantId, po.Id);
    }

    public async Task<Result<PurchaseOrderDto>> CreateDraftFromSuggestionsAsync(
        Guid tenantId, CreatePoFromSuggestionsRequest request)
    {
        var suggestions = await BuildReorderSuggestionLinesAsync(tenantId, request.ProductIds);
        if (suggestions.Count == 0)
            return Result<PurchaseOrderDto>.Failure(
                "No reorder suggestions for selected products / لا توجد اقتراحات إعادة طلب للمنتجات المحددة");

        var lines = suggestions.Select(s => new CreatePurchaseOrderLineRequest
        {
            ProductId = s.ProductId,
            QtyOrdered = s.SuggestedQty,
            UnitCost = s.CostPrice ?? 0m
        }).ToList();

        return await CreateDraftAsync(tenantId, new CreatePurchaseOrderRequest
        {
            SupplierId = request.SupplierId,
            WarehouseId = request.WarehouseId,
            Notes = string.IsNullOrWhiteSpace(request.Notes)
                ? "Auto draft from reorder suggestions / مسودة من اقتراحات إعادة الطلب"
                : request.Notes.Trim(),
            Lines = lines
        });
    }

    private async Task<List<InventoryReorderSuggestionDto>> BuildReorderSuggestionLinesAsync(
        Guid tenantId, List<Guid>? productIds)
    {
        // from-suggestions always needs CostPrice for UnitCost; calculator is the only qty SoT.
        var calc = await _reorder.CalculateAsync(tenantId, productIds, includeCost: true);
        if (!calc.IsSuccess || calc.Data == null)
            return new List<InventoryReorderSuggestionDto>();

        return calc.Data.Select(r => r.ToSuggestionDto()).ToList();
    }

    public async Task<Result<PurchaseOrderDto>> GetAsync(Guid tenantId, Guid id)
    {
        var entity = await LoadPoAsync(tenantId, id, tracking: false);
        if (entity == null)
            return Result<PurchaseOrderDto>.Failure("Purchase order not found / أمر الشراء غير موجود");
        return Result<PurchaseOrderDto>.Success(Map(entity));
    }

    public async Task<Result<InventoryListPageDto<PurchaseOrderDto>>> ListAsync(Guid tenantId, string? status = null)
    {
        var q = _db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Warehouse)
            .Include(p => p.Lines).ThenInclude(l => l.Product)
            .Where(p => p.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(p => p.Status == status.Trim().ToLowerInvariant());

        const int take = 200;
        var rows = await q.OrderByDescending(p => p.OrderedAtUtc).Take(take + 1).ToListAsync();
        var truncated = rows.Count > take;
        if (truncated)
            rows = rows.Take(take).ToList();
        return Result<InventoryListPageDto<PurchaseOrderDto>>.Success(new InventoryListPageDto<PurchaseOrderDto>
        {
            Items = rows.Select(Map).ToList(),
            Truncated = truncated,
            Take = take
        });
    }

    /// <summary>AP-2 Buy docs list — GRNs as purchase presentation (no PurchaseInvoice entity).</summary>
    public async Task<Result<InventoryListPageDto<GoodsReceiptListItemDto>>> ListGoodsReceiptsAsync(
        Guid tenantId, DateTime? fromUtc = null, DateTime? toUtc = null, Guid? supplierId = null)
    {
        var q = _db.GoodsReceipts.AsNoTracking()
            .Include(g => g.Lines)
            .Include(g => g.Warehouse)
            .Include(g => g.PurchaseOrder)!.ThenInclude(p => p!.Supplier)
            .Where(g => g.TenantId == tenantId);

        if (fromUtc.HasValue)
            q = q.Where(g => g.ReceivedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            q = q.Where(g => g.ReceivedAtUtc <= toUtc.Value);
        if (supplierId.HasValue)
            q = q.Where(g => g.PurchaseOrder != null && g.PurchaseOrder.SupplierId == supplierId.Value);

        const int take = 200;
        var rows = await q.OrderByDescending(g => g.ReceivedAtUtc).Take(take + 1).ToListAsync();
        var truncated = rows.Count > take;
        if (truncated)
            rows = rows.Take(take).ToList();

        var items = rows.Select(g =>
        {
            var total = g.Lines.Sum(l => l.Qty * l.UnitCost);
            return new GoodsReceiptListItemDto
            {
                Id = g.Id,
                PurchaseOrderId = g.PurchaseOrderId,
                SupplierId = g.PurchaseOrder?.SupplierId ?? Guid.Empty,
                SupplierName = g.PurchaseOrder?.Supplier?.Name,
                WarehouseId = g.WarehouseId,
                WarehouseCode = g.Warehouse?.Code,
                ReceivedAtUtc = g.ReceivedAtUtc,
                TotalAmount = total,
                Status = "received",
                DocKind = "purchase_doc"
            };
        }).ToList();

        return Result<InventoryListPageDto<GoodsReceiptListItemDto>>.Success(
            new InventoryListPageDto<GoodsReceiptListItemDto>
            {
                Items = items,
                Truncated = truncated,
                Take = take
            });
    }

    public async Task<Result<PurchaseOrderDto>> ApproveAsync(
        Guid tenantId, Guid identityUserId, Guid id)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<PurchaseOrderDto>.Failure("Staff user not found / المستخدم غير موجود");

        var entity = await _db.PurchaseOrders
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (entity == null)
            return Result<PurchaseOrderDto>.Failure("Purchase order not found / أمر الشراء غير موجود");

        if (!string.Equals(entity.Status, PurchaseOrderStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            return Result<PurchaseOrderDto>.Failure(
                $"Cannot approve PO in status {entity.Status} / لا يمكن اعتماد أمر الشراء بحالة {entity.Status}");

        if (entity.Lines.Count == 0)
            return Result<PurchaseOrderDto>.Failure("Purchase order has no lines / أمر الشراء بلا أسطر");

        entity.Status = PurchaseOrderStatuses.Approved;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        entity.ApprovedByUserId = staff.Id;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("purchase_order.approve", "PurchaseOrder", entity.Id, null, new { By = staff.Id });

        return await GetAsync(tenantId, entity.Id);
    }

    public async Task<Result<PurchaseOrderDto>> CancelAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.PurchaseOrders
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (entity == null)
            return Result<PurchaseOrderDto>.Failure("Purchase order not found / أمر الشراء غير موجود");

        if (string.Equals(entity.Status, PurchaseOrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entity.Status, PurchaseOrderStatuses.Received, StringComparison.OrdinalIgnoreCase))
            return Result<PurchaseOrderDto>.Failure(
                $"Cannot cancel PO in status {entity.Status} / لا يمكن إلغاء أمر الشراء بحالة {entity.Status}");

        if (entity.Lines.Any(l => l.QtyReceived > 0))
            return Result<PurchaseOrderDto>.Failure(
                "Cannot cancel after partial receive / لا يمكن الإلغاء بعد استلام جزئي");

        entity.Status = PurchaseOrderStatuses.Cancelled;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("purchase_order.cancel", "PurchaseOrder", entity.Id, null, null);

        return await GetAsync(tenantId, entity.Id);
    }

    public async Task<Result<GoodsReceiptDto>> ReceiveAsync(
        Guid tenantId, Guid identityUserId, Guid purchaseOrderId, ReceivePurchaseOrderRequest request)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId);
        if (staff == null)
            return Result<GoodsReceiptDto>.Failure("Staff user not found / المستخدم غير موجود");

        if (request.Lines == null || request.Lines.Count == 0)
            return Result<GoodsReceiptDto>.Failure("At least one receipt line is required / مطلوب سطر استلام واحد على الأقل");

        var po = await _db.PurchaseOrders
            .Include(p => p.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(p => p.Id == purchaseOrderId && p.TenantId == tenantId);
        if (po == null)
            return Result<GoodsReceiptDto>.Failure("Purchase order not found / أمر الشراء غير موجود");

        if (!PurchaseOrderStatuses.Receivable.Contains(po.Status))
            return Result<GoodsReceiptDto>.Failure(
                $"Cannot receive PO in status {po.Status} / لا يمكن الاستلام بحالة {po.Status}");

        var poLines = po.Lines.ToDictionary(l => l.Id);
        var receiveTotals = new Dictionary<Guid, decimal>();

        foreach (var reqLine in request.Lines)
        {
            if (reqLine.Qty <= 0)
                return Result<GoodsReceiptDto>.Failure("Receive qty must be positive / كمية الاستلام يجب أن تكون موجبة");

            if (!poLines.TryGetValue(reqLine.PurchaseOrderLineId, out var poLine))
                return Result<GoodsReceiptDto>.Failure("Purchase order line not found / سطر أمر الشراء غير موجود");

            var product = poLine.Product!;
            var unitCost = reqLine.UnitCost ?? poLine.UnitCost;
            if (unitCost < 0)
                return Result<GoodsReceiptDto>.Failure("UnitCost cannot be negative / التكلفة لا يمكن أن تكون سالبة");

            if (!product.AllowFractionalQty && decimal.Truncate(reqLine.Qty) != reqLine.Qty)
                return Result<GoodsReceiptDto>.Failure(
                    $"Fractional quantity not allowed for {product.Sku} / الكسور غير مسموحة للمنتج {product.Sku}");

            if (product.TrackBatch && string.IsNullOrWhiteSpace(reqLine.BatchNumber))
                return Result<GoodsReceiptDto>.Failure(
                    $"Batch number required for {product.Sku} / رقم التشغيلة مطلوب للمنتج {product.Sku}");

            if (product.TrackExpiry && !reqLine.ExpiresOn.HasValue)
                return Result<GoodsReceiptDto>.Failure(
                    $"Expiry date required for {product.Sku} / تاريخ الصلاحية مطلوب للمنتج {product.Sku}");

            receiveTotals.TryGetValue(poLine.Id, out var alreadyInRequest);
            var remaining = poLine.QtyOrdered - poLine.QtyReceived - alreadyInRequest;
            if (reqLine.Qty > remaining)
                return Result<GoodsReceiptDto>.Failure(
                    $"Over-receive on line {product.Sku}: remaining {remaining} / تجاوز الكمية المتبقية للمنتج {product.Sku}: {remaining}");

            receiveTotals[poLine.Id] = alreadyInRequest + reqLine.Qty;
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction == null)
            tx = await _db.Database.BeginTransactionAsync();

        try
        {
            var grn = new GoodsReceipt
            {
                TenantId = tenantId,
                PurchaseOrderId = po.Id,
                WarehouseId = po.WarehouseId,
                ReceivedAtUtc = DateTime.UtcNow,
                ReceivedByUserId = staff.Id,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.GoodsReceipts.Add(grn);
            await _db.SaveChangesAsync();

            decimal ledgerAmount = 0;

            foreach (var reqLine in request.Lines)
            {
                var poLine = poLines[reqLine.PurchaseOrderLineId];
                var product = poLine.Product!;
                var unitCost = reqLine.UnitCost ?? poLine.UnitCost;
                var batchNumber = string.IsNullOrWhiteSpace(reqLine.BatchNumber)
                    ? null
                    : reqLine.BatchNumber.Trim();

                Guid? batchId = null;
                if (product.TrackBatch || (product.TrackExpiry && batchNumber != null))
                {
                    if (batchNumber == null)
                        return await FailReceive(tx, "Batch number required / رقم التشغيلة مطلوب");

                    var batch = await _db.ProductBatches
                        .FirstOrDefaultAsync(b =>
                            b.TenantId == tenantId
                            && b.ProductId == product.Id
                            && b.BatchNumber == batchNumber);

                    if (batch == null)
                    {
                        batch = new ProductBatch
                        {
                            TenantId = tenantId,
                            ProductId = product.Id,
                            BatchNumber = batchNumber,
                            ExpiresOn = reqLine.ExpiresOn,
                            CreatedAtUtc = DateTime.UtcNow
                        };
                        _db.ProductBatches.Add(batch);
                        await _db.SaveChangesAsync();
                    }
                    else if (reqLine.ExpiresOn.HasValue && batch.ExpiresOn != reqLine.ExpiresOn)
                    {
                        batch.ExpiresOn = reqLine.ExpiresOn;
                        batch.UpdatedAtUtc = DateTime.UtcNow;
                    }

                    batchId = batch.Id;
                }

                var grnLine = new GoodsReceiptLine
                {
                    TenantId = tenantId,
                    GoodsReceiptId = grn.Id,
                    PurchaseOrderLineId = poLine.Id,
                    ProductId = product.Id,
                    Qty = reqLine.Qty,
                    UnitCost = unitCost,
                    BatchNumber = batchNumber,
                    ExpiresOn = reqLine.ExpiresOn,
                    ProductBatchId = batchId,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _db.GoodsReceiptLines.Add(grnLine);
                await _db.SaveChangesAsync();

                var post = await _ledger.PostAsync(new StockLedgerPostRequest
                {
                    TenantId = tenantId,
                    ProductId = product.Id,
                    WarehouseId = po.WarehouseId,
                    BatchId = batchId,
                    QtyDelta = reqLine.Qty,
                    UnitCost = unitCost,
                    Reason = StockMovementReasons.PurchaseReceipt,
                    ReferenceType = StockReferenceTypes.GoodsReceiptLine,
                    ReferenceId = grnLine.Id,
                    Note = $"PO {po.Id:N}",
                    CreatedByUserId = staff.Id,
                    OccurredAtUtc = grn.ReceivedAtUtc
                });

                if (!post.IsSuccess)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return Result<GoodsReceiptDto>.Failure(post.Error!);
                }

                // High Close H2 — atomic remaining check to block concurrent over-receive.
                if (_db.Database.IsRelational())
                {
                    var nowQty = DateTime.UtcNow;
                    var claimed = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE purchase_order_lines
SET QtyReceived = QtyReceived + {reqLine.Qty}, UpdatedAtUtc = {nowQty}
WHERE Id = {poLine.Id} AND TenantId = {tenantId}
  AND IsDeleted = 0
  AND QtyOrdered - QtyReceived >= {reqLine.Qty}");
                    if (claimed != 1)
                    {
                        if (tx != null) await tx.RollbackAsync();
                        return Result<GoodsReceiptDto>.Failure(
                            $"Over-receive race on line {product.Sku} / تعارض استلام للمنتج {product.Sku}");
                    }

                    await _db.Entry(poLine).ReloadAsync();
                }
                else
                {
                    await _db.Entry(poLine).ReloadAsync();
                    var remainingLive = poLine.QtyOrdered - poLine.QtyReceived;
                    if (reqLine.Qty > remainingLive)
                    {
                        if (tx != null) await tx.RollbackAsync();
                        return Result<GoodsReceiptDto>.Failure(
                            $"Over-receive on line {product.Sku}: remaining {remainingLive} / تجاوز الكمية المتبقية للمنتج {product.Sku}: {remainingLive}");
                    }

                    poLine.QtyReceived += reqLine.Qty;
                }

                product.CostPrice = unitCost;
                product.UpdatedAtUtc = DateTime.UtcNow;
                ledgerAmount += unitCost * reqLine.Qty;
            }

            var allReceived = po.Lines.All(l => l.QtyReceived >= l.QtyOrdered);
            var anyReceived = po.Lines.Any(l => l.QtyReceived > 0);
            po.Status = allReceived
                ? PurchaseOrderStatuses.Received
                : anyReceived
                    ? PurchaseOrderStatuses.PartiallyReceived
                    : po.Status;
            po.UpdatedAtUtc = DateTime.UtcNow;

            if (ledgerAmount > 0)
            {
                _db.SupplierLedgerEntries.Add(new SupplierLedgerEntry
                {
                    TenantId = tenantId,
                    SupplierId = po.SupplierId,
                    Amount = ledgerAmount,
                    Reason = SupplierLedgerReasons.Purchase,
                    ReferenceType = "GoodsReceipt",
                    ReferenceId = grn.Id,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();

            if (tx != null)
                await tx.CommitAsync();

            await _audit.LogAsync("purchase_order.receive", "GoodsReceipt", grn.Id, null,
                new { po.Id, Lines = request.Lines.Count, po.Status });

            _logger.LogInformation(
                "Received GRN {GrnId} for PO {PoId} status {Status}", grn.Id, po.Id, po.Status);

            return await GetGoodsReceiptAsync(tenantId, grn.Id);
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Failed receiving PO {Id}", purchaseOrderId);
            return Result<GoodsReceiptDto>.Failure(
                "Failed to receive purchase order / فشل استلام أمر الشراء", ex.Message);
        }
        finally
        {
            if (tx != null)
                await tx.DisposeAsync();
        }
    }

    private async Task<Result<GoodsReceiptDto>> FailReceive(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx, string error)
    {
        if (tx != null) await tx.RollbackAsync();
        return Result<GoodsReceiptDto>.Failure(error);
    }

    public async Task<Result<GoodsReceiptDto>> GetGoodsReceiptAsync(Guid tenantId, Guid id)
    {
        var grn = await _db.GoodsReceipts.AsNoTracking()
            .Include(g => g.Lines)
            .Include(g => g.Warehouse)
            .Include(g => g.PurchaseOrder)!.ThenInclude(p => p!.Supplier)
            .FirstOrDefaultAsync(g => g.Id == id && g.TenantId == tenantId);
        if (grn == null)
            return Result<GoodsReceiptDto>.Failure("Goods receipt not found / إذن الاستلام غير موجود");

        return Result<GoodsReceiptDto>.Success(new GoodsReceiptDto
        {
            Id = grn.Id,
            PurchaseOrderId = grn.PurchaseOrderId,
            WarehouseId = grn.WarehouseId,
            ReceivedAtUtc = grn.ReceivedAtUtc,
            SupplierId = grn.PurchaseOrder?.SupplierId,
            SupplierName = grn.PurchaseOrder?.Supplier?.Name,
            WarehouseCode = grn.Warehouse?.Code,
            TotalAmount = grn.Lines.Sum(l => l.Qty * l.UnitCost),
            Status = "received",
            DocKind = "purchase_doc",
            Lines = grn.Lines.Select(l => new GoodsReceiptLineDto
            {
                Id = l.Id,
                PurchaseOrderLineId = l.PurchaseOrderLineId,
                ProductId = l.ProductId,
                Qty = l.Qty,
                UnitCost = l.UnitCost,
                BatchNumber = l.BatchNumber,
                ExpiresOn = l.ExpiresOn,
                ProductBatchId = l.ProductBatchId
            }).ToList()
        });
    }

    private async Task<PurchaseOrder?> LoadPoAsync(Guid tenantId, Guid id, bool tracking)
    {
        IQueryable<PurchaseOrder> q = tracking ? _db.PurchaseOrders : _db.PurchaseOrders.AsNoTracking();
        return await q
            .Include(p => p.Supplier)
            .Include(p => p.Warehouse)
            .Include(p => p.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
    }

    private async Task<AppUser?> ResolveAppUserAsync(Guid tenantId, Guid identityUserId)
    {
        var key = identityUserId.ToString();
        return await _db.AppUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == key);
    }

    private static PurchaseOrderDto Map(PurchaseOrder p) => new()
    {
        Id = p.Id,
        SupplierId = p.SupplierId,
        SupplierName = p.Supplier?.Name,
        WarehouseId = p.WarehouseId,
        WarehouseCode = p.Warehouse?.Code,
        Status = p.Status,
        OrderedAtUtc = p.OrderedAtUtc,
        ApprovedAtUtc = p.ApprovedAtUtc,
        Notes = p.Notes,
        Lines = p.Lines.Select(l => new PurchaseOrderLineDto
        {
            Id = l.Id,
            ProductId = l.ProductId,
            ProductSku = l.Product?.Sku,
            ProductName = l.Product?.Name,
            QtyOrdered = l.QtyOrdered,
            QtyReceived = l.QtyReceived,
            UnitCost = l.UnitCost
        }).ToList()
    };
}
