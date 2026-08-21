namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>G1 — single reorder calculator (velocity × lead + Incoming + ReorderMin floor).</summary>
public class InventoryReorderCalculator : IInventoryReorderCalculator
{
    private readonly GymFlowProDbContext _db;

    public InventoryReorderCalculator(GymFlowProDbContext db) => _db = db;

    public async Task<Result<List<ReorderCalcRow>>> CalculateAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid>? productIds = null,
        bool includeCost = false,
        CancellationToken ct = default)
    {
        var productsQ = _db.Products.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.TrackStock && p.IsActive && !p.IsArchived
                && p.IsPurchasable && p.ReorderMinQty > 0);

        if (productIds is { Count: > 0 })
            productsQ = productsQ.Where(p => productIds.Contains(p.Id));

        var products = await productsQ
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Name,
                p.NameAr,
                p.ImageUrl,
                p.ReorderMinQty,
                p.CostPrice,
                p.SellPrice,
                p.TrackExpiry
            })
            .ToListAsync(ct);

        if (products.Count == 0)
            return Result<List<ReorderCalcRow>>.Success(new List<ReorderCalcRow>());

        var ids = products.Select(p => p.Id).ToList();

        var balances = await _db.StockBalances.AsNoTracking()
            .Where(b => b.TenantId == tenantId && ids.Contains(b.ProductId))
            .Select(b => new { b.ProductId, b.BatchId, b.QtyOnHand })
            .ToListAsync(ct);

        var batchIds = balances.Where(b => b.BatchId.HasValue).Select(b => b.BatchId!.Value).Distinct().ToList();
        var batchExpiry = batchIds.Count == 0
            ? new Dictionary<Guid, DateOnly?>()
            : await _db.ProductBatches.AsNoTracking()
                .Where(b => b.TenantId == tenantId && batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.ExpiresOn, ct);

        var today = MembershipOperational.TodayCairo();
        var trackExpiry = products.ToDictionary(p => p.Id, p => p.TrackExpiry);

        decimal AvailableFor(Guid productId)
        {
            trackExpiry.TryGetValue(productId, out var te);
            return balances.Where(b => b.ProductId == productId).Where(b =>
            {
                if (!b.BatchId.HasValue) return true;
                if (!te) return true;
                if (!batchExpiry.TryGetValue(b.BatchId.Value, out var exp)) return false;
                if (!exp.HasValue) return true;
                return exp.Value >= today;
            }).Sum(b => b.QtyOnHand);
        }

        decimal OnHandFor(Guid productId) =>
            balances.Where(b => b.ProductId == productId).Sum(b => b.QtyOnHand);

        var sinceUtc = DateTime.UtcNow.AddDays(-InventoryReorderDefaults.LookbackDays);
        var soldByProduct = await _db.StockMovements.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                && ids.Contains(m.ProductId)
                && m.Reason == StockMovementReasons.Sale
                && m.OccurredAtUtc >= sinceUtc
                && m.QtyDelta < 0)
            .GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Sold = g.Sum(x => -x.QtyDelta) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Sold, ct);

        var openPoStatuses = new[]
        {
            PurchaseOrderStatuses.Approved,
            PurchaseOrderStatuses.PartiallyReceived
        };

        // Join — avoid nav filters that break InMemory tests and fragile EF translation.
        var incomingPo = await (
            from l in _db.PurchaseOrderLines.AsNoTracking()
            join po in _db.PurchaseOrders.AsNoTracking() on l.PurchaseOrderId equals po.Id
            where po.TenantId == tenantId
                  && openPoStatuses.Contains(po.Status)
                  && ids.Contains(l.ProductId)
            group l by l.ProductId into g
            select new
            {
                ProductId = g.Key,
                Qty = g.Sum(x => x.QtyOrdered - x.QtyReceived)
            }).ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);

        var incomingXfer = await (
            from l in _db.StockTransferLines.AsNoTracking()
            join t in _db.StockTransfers.AsNoTracking() on l.StockTransferId equals t.Id
            where t.TenantId == tenantId
                  && t.Status == StockTransferStatuses.InTransit
                  && ids.Contains(l.ProductId)
            group l by l.ProductId into g
            select new
            {
                ProductId = g.Key,
                Qty = g.Sum(x => x.Qty)
            }).ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);

        var lookback = (decimal)InventoryReorderDefaults.LookbackDays;
        var lead = (decimal)InventoryReorderDefaults.LeadTimeDays;
        var list = new List<ReorderCalcRow>();

        foreach (var p in products)
        {
            var available = AvailableFor(p.Id);
            var onHand = OnHandFor(p.Id);
            soldByProduct.TryGetValue(p.Id, out var sold);
            var avgDaily = sold <= 0 ? 0m : Math.Round(sold / lookback, 3, MidpointRounding.AwayFromZero);
            decimal? daysOfCover = avgDaily > 0
                ? Math.Round(available / avgDaily, 1, MidpointRounding.AwayFromZero)
                : null;

            incomingPo.TryGetValue(p.Id, out var poIn);
            incomingXfer.TryGetValue(p.Id, out var xferIn);
            var incoming = Math.Max(0m, poIn) + Math.Max(0m, xferIn);

            var velocityNeed = avgDaily > 0
                ? Math.Ceiling(avgDaily * lead - available - incoming)
                : 0m;
            if (velocityNeed < 0) velocityNeed = 0;

            var minGap = p.ReorderMinQty - available - incoming;
            if (minGap < 0) minGap = 0;

            var suggested = Math.Max(velocityNeed, minGap);
            if (suggested <= 0)
                continue;

            list.Add(new ReorderCalcRow
            {
                ProductId = p.Id,
                Sku = p.Sku,
                Name = p.Name,
                NameAr = p.NameAr,
                ImageUrl = p.ImageUrl,
                OnHand = onHand,
                Available = available,
                ReorderMinQty = p.ReorderMinQty,
                SuggestedQty = suggested,
                CostPrice = includeCost ? p.CostPrice : null,
                SellPrice = p.SellPrice,
                AvgDailySales = avgDaily,
                DaysOfCover = daysOfCover,
                IncomingOpenQty = incoming
            });
        }

        return Result<List<ReorderCalcRow>>.Success(
            list.OrderBy(x => x.Available).ThenBy(x => x.Sku).ToList());
    }
}
