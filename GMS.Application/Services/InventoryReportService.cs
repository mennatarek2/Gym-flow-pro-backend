namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>INVS-10 inventory reports + daily low-stock/expiry alerts.</summary>
public class InventoryReportService : IInventoryReportService
{
    private static readonly TimeZoneInfo CairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
    private static readonly int[] DefaultExpiryWindows = { 90, 30, 7 };
    private static readonly string[] DefaultNotifyRoles = { "Owner", "Manager" };

    private readonly GymFlowProDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IInventoryReorderCalculator _reorder;
    private readonly ILogger<InventoryReportService> _logger;

    public InventoryReportService(
        GymFlowProDbContext db,
        INotificationService notifications,
        IInventoryReorderCalculator reorder,
        ILogger<InventoryReportService> logger)
    {
        _db = db;
        _notifications = notifications;
        _reorder = reorder;
        _logger = logger;
    }

    public async Task<Result<InventorySummaryReportDto>> GetSummaryAsync(Guid tenantId, bool includeValuation)
    {
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.TrackStock && p.IsActive && !p.IsArchived)
            .Select(p => new { p.Id, p.CostPrice, p.ReorderMinQty, p.TrackExpiry })
            .ToListAsync();

        var balances = await _db.StockBalances.AsNoTracking()
            .Where(b => b.TenantId == tenantId)
            .Select(b => new { b.ProductId, b.BatchId, b.QtyOnHand })
            .ToListAsync();

        var batchIds = balances.Where(b => b.BatchId.HasValue).Select(b => b.BatchId!.Value).Distinct().ToList();
        var batchExpiry = batchIds.Count == 0
            ? new Dictionary<Guid, DateOnly?>()
            : await _db.ProductBatches.AsNoTracking()
                .Where(b => b.TenantId == tenantId && batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.ExpiresOn);

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

        decimal? value = null;
        if (includeValuation)
        {
            value = 0m;
            var costByProduct = products.ToDictionary(p => p.Id, p => p.CostPrice);
            foreach (var p in products)
            {
                if (!costByProduct.TryGetValue(p.Id, out var cost)) continue;
                value += AvailableFor(p.Id) * cost;
            }
            value = Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);
        }

        var outOfStock = 0;
        var lowStock = 0;
        foreach (var p in products)
        {
            var available = AvailableFor(p.Id);
            if (available <= 0)
                outOfStock++;
            if (p.ReorderMinQty > 0 && available <= p.ReorderMinQty)
                lowStock++;
        }

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        var windows = GetExpiryWindows(tenant?.Settings);

        var batchExpiries = await _db.ProductBatches.AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.ExpiresOn != null)
            .Select(b => b.ExpiresOn!.Value)
            .ToListAsync();

        var expiring = windows.Select(days => new InventoryExpiryWindowDto
        {
            Days = days,
            BatchCount = batchExpiries.Count(exp => exp >= today && exp <= today.AddDays(days))
        }).ToList();

        GetCairoDayUtcRange(out var dayStartUtc, out var dayEndUtc);

        var retailToday = await _db.SaleLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId
                && l.LineType == "retail"
                && l.Sale != null
                && l.Sale.CreatedAtUtc >= dayStartUtc
                && l.Sale.CreatedAtUtc < dayEndUtc
                && l.Sale.Status != "refunded")
            .Select(l => new { l.Qty, l.LineTotal })
            .ToListAsync();

        var todayUnits = retailToday.Sum(x => (decimal)x.Qty);
        var todaySales = Math.Round(retailToday.Sum(x => x.LineTotal), 2, MidpointRounding.AwayFromZero);

        var pendingStatuses = new[]
        {
            PurchaseOrderStatuses.Draft,
            PurchaseOrderStatuses.Approved,
            PurchaseOrderStatuses.PartiallyReceived
        };
        var pendingPo = await _db.PurchaseOrders.AsNoTracking()
            .CountAsync(p => p.TenantId == tenantId && pendingStatuses.Contains(p.Status));

        var inTransit = await _db.StockTransfers.AsNoTracking()
            .CountAsync(t => t.TenantId == tenantId && t.Status == StockTransferStatuses.InTransit);

        return Result<InventorySummaryReportDto>.Success(new InventorySummaryReportDto
        {
            InventoryValueEgp = value,
            OutOfStockCount = outOfStock,
            LowStockCount = lowStock,
            ExpiringSoon = expiring,
            IncludesValuation = includeValuation,
            TodayRetailUnits = todayUnits,
            TodayRetailSalesEgp = includeValuation ? todaySales : null,
            PendingPoCount = pendingPo,
            InTransitTransferCount = inTransit
        });
    }

    public async Task<Result<List<InventoryReorderSuggestionDto>>> GetReorderSuggestionsAsync(
        Guid tenantId, bool includeCost = false)
    {
        var calc = await _reorder.CalculateAsync(tenantId, productIds: null, includeCost);
        if (!calc.IsSuccess)
            return Result<List<InventoryReorderSuggestionDto>>.Failure(calc.Error!);

        return Result<List<InventoryReorderSuggestionDto>>.Success(
            calc.Data!.Select(r => r.ToSuggestionDto()).ToList());
    }

    public async Task<Result<List<InventoryDeadStockRowDto>>> GetDeadStockAsync(
        Guid tenantId, int daysIdle = 30, bool includeCost = false)
    {
        daysIdle = daysIdle <= 0 ? 30 : Math.Min(daysIdle, 365);
        var sinceUtc = DateTime.UtcNow.AddDays(-daysIdle);

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.TrackStock && p.IsActive && !p.IsArchived)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Name,
                p.NameAr,
                p.ImageUrl,
                p.CostPrice
            })
            .ToListAsync();

        var qtyByProduct = await _db.StockBalances.AsNoTracking()
            .Where(b => b.TenantId == tenantId)
            .GroupBy(b => b.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.QtyOnHand) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty);

        var lastSaleByProduct = await _db.StockMovements.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Reason == StockMovementReasons.Sale)
            .GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, LastAt = g.Max(x => x.OccurredAtUtc) })
            .ToDictionaryAsync(x => x.ProductId, x => x.LastAt);

        var rows = new List<InventoryDeadStockRowDto>();
        foreach (var p in products)
        {
            qtyByProduct.TryGetValue(p.Id, out var onHand);
            if (onHand <= 0) continue;

            lastSaleByProduct.TryGetValue(p.Id, out var lastSold);
            if (lastSold != default && lastSold >= sinceUtc) continue;

            var idle = lastSold == default
                ? daysIdle
                : (int)Math.Floor((DateTime.UtcNow - lastSold).TotalDays);

            rows.Add(new InventoryDeadStockRowDto
            {
                ProductId = p.Id,
                Sku = p.Sku,
                Name = p.Name,
                NameAr = p.NameAr,
                ImageUrl = p.ImageUrl,
                OnHand = onHand,
                CostPrice = includeCost ? p.CostPrice : null,
                LastSoldAtUtc = lastSold == default ? null : lastSold,
                DaysIdle = idle
            });
        }

        return Result<List<InventoryDeadStockRowDto>>.Success(
            rows.OrderByDescending(r => r.DaysIdle).ThenByDescending(r => r.OnHand).Take(100).ToList());
    }

    public async Task<Result<List<InventoryProductPerformanceRowDto>>> GetProductPerformanceAsync(
        Guid tenantId, DateTime fromUtc, DateTime toUtc, bool includeMargin, int take = 50)
    {
        if (toUtc < fromUtc)
            return Result<List<InventoryProductPerformanceRowDto>>.Failure(
                "ToUtc must be on or after FromUtc / تاريخ النهاية يجب أن يكون بعد البداية");

        if ((toUtc - fromUtc).TotalDays > 366)
            return Result<List<InventoryProductPerformanceRowDto>>.Failure(
                "Date range cannot exceed 366 days / لا يمكن أن يتجاوز النطاق 366 يوماً");

        take = take <= 0 ? 50 : Math.Min(take, 200);

        var lines = await _db.SaleLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId
                && l.LineType == "retail"
                && l.ReferenceId != null
                && l.Sale != null
                && l.Sale.CreatedAtUtc >= fromUtc
                && l.Sale.CreatedAtUtc < toUtc
                && l.Sale.Status != "refunded")
            .Select(l => new
            {
                ProductId = l.ReferenceId!.Value,
                l.Qty,
                l.LineTotal,
                l.Sale!.CreatedAtUtc
            })
            .ToListAsync();

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var grouped = lines
            .GroupBy(l => l.ProductId)
            .Select(g =>
            {
                products.TryGetValue(g.Key, out var p);
                var qty = g.Sum(x => (decimal)x.Qty);
                var revenue = g.Sum(x => x.LineTotal);
                decimal? margin = null;
                if (includeMargin && p != null)
                    margin = Math.Round(revenue - qty * p.CostPrice, 2, MidpointRounding.AwayFromZero);

                return new InventoryProductPerformanceRowDto
                {
                    ProductId = g.Key,
                    Sku = p?.Sku ?? "",
                    Name = p?.Name ?? "",
                    NameAr = p?.NameAr,
                    ImageUrl = p?.ImageUrl,
                    QtySold = qty,
                    RevenueEgp = Math.Round(revenue, 2, MidpointRounding.AwayFromZero),
                    EstMarginEgp = margin,
                    LastSoldAtUtc = g.Max(x => x.CreatedAtUtc)
                };
            })
            .OrderByDescending(r => r.QtySold)
            .Take(take)
            .ToList();

        return Result<List<InventoryProductPerformanceRowDto>>.Success(grouped);
    }

    public async Task<Result<List<InventoryMovementReportRowDto>>> GetMovementsAsync(
        Guid tenantId, InventoryMovementQueryRequest request)
    {
        if (request.ToUtc < request.FromUtc)
            return Result<List<InventoryMovementReportRowDto>>.Failure(
                "ToUtc must be on or after FromUtc / تاريخ النهاية يجب أن يكون بعد البداية");

        if ((request.ToUtc - request.FromUtc).TotalDays > 366)
            return Result<List<InventoryMovementReportRowDto>>.Failure(
                "Date range cannot exceed 366 days / لا يمكن أن يتجاوز النطاق 366 يوماً");

        if (!string.IsNullOrWhiteSpace(request.Reason)
            && !StockMovementReasons.All.Contains(request.Reason.Trim()))
            return Result<List<InventoryMovementReportRowDto>>.Failure(
                "Invalid movement reason / سبب الحركة غير صالح");

        var take = request.Take <= 0 ? 200 : Math.Min(request.Take, 1000);

        var q = _db.StockMovements.AsNoTracking()
            .Include(m => m.Product)
            .Include(m => m.Warehouse)
            .Where(m => m.TenantId == tenantId
                && m.OccurredAtUtc >= request.FromUtc
                && m.OccurredAtUtc <= request.ToUtc);

        if (request.ProductId.HasValue)
            q = q.Where(m => m.ProductId == request.ProductId.Value);
        if (request.WarehouseId.HasValue)
            q = q.Where(m => m.WarehouseId == request.WarehouseId.Value);
        if (!string.IsNullOrWhiteSpace(request.Reason))
            q = q.Where(m => m.Reason == request.Reason.Trim().ToLowerInvariant());

        var rows = await q.OrderByDescending(m => m.OccurredAtUtc).Take(take).ToListAsync();

        return Result<List<InventoryMovementReportRowDto>>.Success(rows.Select(m => new InventoryMovementReportRowDto
        {
            Id = m.Id,
            ProductId = m.ProductId,
            ProductSku = m.Product?.Sku,
            ProductName = m.Product?.Name,
            WarehouseId = m.WarehouseId,
            WarehouseCode = m.Warehouse?.Code,
            BatchId = m.BatchId,
            QtyDelta = m.QtyDelta,
            UnitCost = m.UnitCost,
            Reason = m.Reason,
            ReferenceType = m.ReferenceType,
            ReferenceId = m.ReferenceId,
            Note = m.Note,
            OccurredAtUtc = m.OccurredAtUtc
        }).ToList());
    }

    public async Task<Result<InventoryAlertJobResultDto>> RunDailyAlertsAsync(Guid tenantId, DateOnly cairoDate)
    {
        var result = new InventoryAlertJobResultDto { TenantId = tenantId };
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null)
            return Result<InventoryAlertJobResultDto>.Failure("Tenant not found");

        var roles = GetNotifyRoles(tenant.Settings);
        var staff = await _db.AppUsers
            .Where(u => u.TenantId == tenantId && u.IsActive && roles.Contains(u.Role))
            .ToListAsync();

        if (staff.Count == 0)
            return Result<InventoryAlertJobResultDto>.Success(result);

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.TrackStock && p.IsActive && !p.IsArchived
                && p.ReorderMinQty > 0)
            .ToListAsync();

        var balances = await _db.StockBalances.AsNoTracking()
            .Where(b => b.TenantId == tenantId)
            .Select(b => new { b.ProductId, b.BatchId, b.QtyOnHand })
            .ToListAsync();

        var batchIds = balances.Where(b => b.BatchId.HasValue).Select(b => b.BatchId!.Value).Distinct().ToList();
        var batchExpiry = batchIds.Count == 0
            ? new Dictionary<Guid, DateOnly?>()
            : await _db.ProductBatches.AsNoTracking()
                .Where(b => b.TenantId == tenantId && batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.ExpiresOn);

        var todayCairo = MembershipOperational.TodayCairo();
        decimal AvailableFor(Guid productId, bool trackExpiry)
        {
            return balances.Where(b => b.ProductId == productId).Where(b =>
            {
                if (!b.BatchId.HasValue) return true;
                if (!trackExpiry) return true;
                if (!batchExpiry.TryGetValue(b.BatchId.Value, out var exp)) return false;
                if (!exp.HasValue) return true;
                return exp.Value >= todayCairo;
            }).Sum(b => b.QtyOnHand);
        }

        var dayKey = cairoDate.ToString("yyyyMMdd");

        foreach (var product in products)
        {
            var available = AvailableFor(product.Id, product.TrackExpiry);
            if (available > product.ReorderMinQty)
                continue;

            var dedupe = $"inv-low:{dayKey}:{product.Id:N}";
            if (await AlreadyNotifiedAsync(tenantId, dedupe))
            {
                result.SkippedDedupe++;
                continue;
            }

            var title = $"Low stock: {product.Sku}";
            var titleAr = $"مخزون منخفض: {product.Sku}";
            var body = $"{product.Name} available {available} (min {product.ReorderMinQty})";
            var bodyAr = $"{product.NameAr ?? product.Name} المتاح {available} (الحد الأدنى {product.ReorderMinQty})";

            foreach (var user in staff)
            {
                await _notifications.CreateForStaffAsync(
                    tenantId, user.Id, title, titleAr, body, bodyAr, dedupe);
            }

            result.LowStockNotified++;
        }

        var windows = GetExpiryWindows(tenant.Settings).OrderBy(d => d).ToList();
        if (windows.Count > 0)
        {
            var maxDays = windows[^1];
            var qtyByBatch = balances
                .Where(b => b.BatchId.HasValue)
                .GroupBy(b => b.BatchId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.QtyOnHand));

            var batches = await _db.ProductBatches.AsNoTracking()
                .Include(b => b.Product)
                .Where(b => b.TenantId == tenantId
                    && b.ExpiresOn != null
                    && b.ExpiresOn >= cairoDate
                    && b.ExpiresOn <= cairoDate.AddDays(maxDays))
                .ToListAsync();

            foreach (var batch in batches)
            {
                qtyByBatch.TryGetValue(batch.Id, out var batchQty);
                if (batchQty <= 0)
                    continue;

                var daysLeft = batch.ExpiresOn!.Value.DayNumber - cairoDate.DayNumber;
                var window = windows.First(w => daysLeft <= w);

                var dedupe = $"inv-exp:{dayKey}:{window}:{batch.Id:N}";
                if (await AlreadyNotifiedAsync(tenantId, dedupe))
                {
                    result.SkippedDedupe++;
                    continue;
                }

                var sku = batch.Product?.Sku ?? "?";
                var name = batch.Product?.Name ?? "Product";
                var title = $"Batch expiring ({window}d): {sku}";
                var titleAr = $"تشغيلة قاربت الصلاحية ({window}ي): {sku}";
                var body = $"{name} batch {batch.BatchNumber} expires {batch.ExpiresOn:yyyy-MM-dd}";
                var bodyAr = $"{batch.Product?.NameAr ?? name} تشغيلة {batch.BatchNumber} تنتهي {batch.ExpiresOn:yyyy-MM-dd}";

                foreach (var user in staff)
                {
                    await _notifications.CreateForStaffAsync(
                        tenantId, user.Id, title, titleAr, body, bodyAr, dedupe);
                }

                result.ExpiryNotified++;
            }
        }

        _logger.LogInformation(
            "Inventory alerts tenant {TenantId}: low={Low} exp={Exp} dedupeSkip={Skip}",
            tenantId, result.LowStockNotified, result.ExpiryNotified, result.SkippedDedupe);

        return Result<InventoryAlertJobResultDto>.Success(result);
    }

    private async Task<bool> AlreadyNotifiedAsync(Guid tenantId, string dedupeKey) =>
        await _db.Notifications.AsNoTracking()
            .AnyAsync(n => n.TenantId == tenantId && n.ExternalMessageId == dedupeKey);

    private static void GetCairoDayUtcRange(out DateTime dayStartUtc, out DateTime dayEndUtc)
    {
        var nowCairo = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTz);
        var startCairo = DateTime.SpecifyKind(nowCairo.Date, DateTimeKind.Unspecified);
        var endCairo = startCairo.AddDays(1);
        dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(startCairo, CairoTz);
        dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(endCairo, CairoTz);
    }

    private static int[] GetExpiryWindows(string? settingsJson)
    {
        var arr = GetJsonIntArray(settingsJson, TenantSettingsKeys.InventoryExpiryWindowsDays);
        return arr is { Length: > 0 } ? arr : DefaultExpiryWindows;
    }

    private static HashSet<string> GetNotifyRoles(string? settingsJson)
    {
        var arr = GetJsonStringArray(settingsJson, TenantSettingsKeys.InventoryLowStockNotifyRoles);
        var roles = arr is { Length: > 0 } ? arr : DefaultNotifyRoles;
        return new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
    }

    private static int[]? GetJsonIntArray(string? settingsJson, string key)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (!doc.RootElement.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Array)
                return null;
            return el.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Number)
                .Select(x => x.GetInt32())
                .Where(x => x > 0)
                .Distinct()
                .OrderByDescending(x => x)
                .ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string[]? GetJsonStringArray(string? settingsJson, string key)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (!doc.RootElement.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Array)
                return null;
            return el.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
