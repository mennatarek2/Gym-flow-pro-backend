namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.MemberStore;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Member App store catalog + operational fulfillment orders (Stage 0).
/// Does not create Sale/Payment and never posts the stock ledger.
/// Availability is a read-only check via <see cref="IStockLedgerService.GetAvailableAsync"/>.
/// </summary>
public class MemberStoreService : IMemberStoreService
{
    private readonly GymFlowProDbContext _db;
    private readonly IStockLedgerService _ledger;
    private readonly IAuditService _audit;
    private readonly IMemberOrderNotifier _notifier;

    public MemberStoreService(
        GymFlowProDbContext db,
        IStockLedgerService ledger,
        IAuditService audit,
        IMemberOrderNotifier notifier)
    {
        _db = db;
        _ledger = ledger;
        _audit = audit;
        _notifier = notifier;
    }

    public async Task<Result<List<MemberStoreProductDto>>> ListStoreProductsAsync(
        Guid tenantId, string? q = null, CancellationToken ct = default)
    {
        var query = _db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p =>
                p.TenantId == tenantId
                && p.VisibleToMembers
                && p.IsActive
                && !p.IsArchived
                && p.IsSellable);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p =>
                p.Name.Contains(term)
                || p.Sku.Contains(term)
                || (p.NameAr != null && p.NameAr.Contains(term))
                || (p.Brand != null && p.Brand.Contains(term)));
        }

        var products = await query
            .OrderBy(p => p.Name)
            .Take(200)
            .ToListAsync(ct);

        var warehouse = await ResolveDefaultWarehouseAsync(tenantId, ct);
        var list = new List<MemberStoreProductDto>(products.Count);

        foreach (var p in products)
        {
            decimal available = 0m;
            var inStock = true;
            if (p.TrackStock)
            {
                if (warehouse == null)
                {
                    available = 0m;
                    inStock = false;
                }
                else
                {
                    var avail = await _ledger.GetAvailableAsync(tenantId, p.Id, warehouse.Id, ct);
                    available = avail.IsSuccess ? avail.Data : 0m;
                    inStock = available > 0m;
                }
            }

            list.Add(new MemberStoreProductDto
            {
                Id = p.Id,
                CategoryId = p.CategoryId,
                CategoryName = p.Category?.Name,
                Sku = p.Sku,
                Name = p.Name,
                NameAr = p.NameAr,
                Description = p.Description,
                DescriptionAr = p.DescriptionAr,
                Brand = p.Brand,
                ImageUrl = p.ImageUrl,
                UnitOfMeasure = p.UnitOfMeasure,
                SellPrice = p.SellPrice,
                Currency = p.Currency,
                AllowFractionalQty = p.AllowFractionalQty,
                TrackStock = p.TrackStock,
                AvailableQty = available,
                InStock = inStock
            });
        }

        return Result<List<MemberStoreProductDto>>.Success(list);
    }

    public async Task<Result<MemberOrderDto>> CreateOrderAsync(
        Guid tenantId, Guid identityUserId, CreateMemberOrderRequest request, CancellationToken ct = default)
    {
        if (request.Lines == null || request.Lines.Count == 0)
            return FailOrder("At least one line is required / مطلوب سطر واحد على الأقل");

        if (request.Lines.Count > 50)
            return FailOrder("Too many lines / عدد البنود كبير جداً");

        var member = await FindMemberByIdentityAsync(tenantId, identityUserId, ct);
        if (member == null)
            return FailOrder("Member account not linked / الحساب غير مرتبط بعضو");

        if (!member.IsActive)
            return FailOrder("Member is inactive / العضو غير نشط");

        var warehouse = await ResolveDefaultWarehouseAsync(tenantId, ct);
        if (warehouse == null)
            return FailOrder("No active warehouse for fulfillments / لا يوجد مخزن نشط للطلبات");

        // Collapse duplicate product lines.
        var merged = request.Lines
            .GroupBy(l => l.ProductId)
            .Select(g => new CreateMemberOrderLineRequest
            {
                ProductId = g.Key,
                Qty = g.Sum(x => x.Qty)
            })
            .ToList();

        if (merged.Any(l => l.ProductId == Guid.Empty || l.Qty <= 0m))
            return FailOrder("Invalid product or quantity / منتج أو كمية غير صالحة");

        var productIds = merged.Select(l => l.ProductId).ToList();
        var products = await _db.Products
            .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
            .ToListAsync(ct);

        if (products.Count != productIds.Count)
            return FailOrder("One or more products were not found / منتج واحد أو أكثر غير موجود");

        var lines = new List<MemberOrderLine>();
        decimal subtotal = 0m;
        string currency = products[0].Currency;

        foreach (var req in merged)
        {
            var product = products.First(p => p.Id == req.ProductId);

            if (!product.VisibleToMembers || !product.IsActive || product.IsArchived || !product.IsSellable)
                return FailOrder($"Product not available in store: {product.Sku} / المنتج غير متاح في المتجر: {product.Sku}");

            if (!product.AllowFractionalQty && decimal.Truncate(req.Qty) != req.Qty)
                return FailOrder($"Fractional qty not allowed for {product.Sku} / الكمية الكسرية غير مسموحة للمنتج {product.Sku}");

            if (product.TrackStock)
            {
                var avail = await _ledger.GetAvailableAsync(tenantId, product.Id, warehouse.Id, ct);
                if (!avail.IsSuccess)
                    return FailOrder(avail.Error!);

                if (avail.Data < req.Qty)
                    return FailOrder(
                        $"Insufficient stock for {product.Sku}: available {avail.Data}, requested {req.Qty} / رصيد غير كافٍ للمنتج {product.Sku}");
            }

            if (!string.Equals(product.Currency, currency, StringComparison.OrdinalIgnoreCase))
                return FailOrder("Mixed currencies are not supported / العملات المختلطة غير مدعومة");

            var unit = product.SellPrice;
            var lineTotal = Math.Round(unit * req.Qty, 2, MidpointRounding.AwayFromZero);
            subtotal += lineTotal;

            lines.Add(new MemberOrderLine
            {
                TenantId = tenantId,
                ProductId = product.Id,
                ProductSku = product.Sku,
                ProductName = product.Name,
                ProductNameAr = product.NameAr,
                UnitPrice = unit,
                Qty = req.Qty,
                LineTotal = lineTotal,
                Currency = product.Currency,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        var orderNumber = await NextOrderNumberAsync(tenantId, ct);
        var order = new MemberOrder
        {
            TenantId = tenantId,
            MemberId = member.Id,
            OrderNumber = orderNumber,
            Status = MemberOrderStatuses.Pending,
            WarehouseId = warehouse.Id,
            Currency = currency,
            Subtotal = subtotal,
            Total = subtotal,
            MemberNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            Lines = lines
        };

        foreach (var line in lines)
            line.MemberOrder = order;

        _db.MemberOrders.Add(order);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "member_order.create",
            "MemberOrder",
            order.Id,
            null,
            new { order.OrderNumber, order.MemberId, order.Total, LineCount = lines.Count },
            tenantIdOverride: tenantId);

        _ = _notifier.NotifyCreatedAsync(
            tenantId, order.Id, order.OrderNumber, member.Id, member.FullName, ct);

        return Result<MemberOrderDto>.Success(await MapOrderAsync(order.Id, tenantId, ct));
    }

    public async Task<Result<List<MemberOrderListItemDto>>> ListMyOrdersAsync(
        Guid tenantId, Guid identityUserId, CancellationToken ct = default)
    {
        var member = await FindMemberByIdentityAsync(tenantId, identityUserId, ct);
        if (member == null)
            return Result<List<MemberOrderListItemDto>>.Failure(
                "Member account not linked / الحساب غير مرتبط بعضو");

        var rows = await _db.MemberOrders.AsNoTracking()
            .Include(o => o.Lines)
            .Include(o => o.Member)
            .Where(o => o.TenantId == tenantId && o.MemberId == member.Id)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);

        return Result<List<MemberOrderListItemDto>>.Success(rows.Select(MapListItem).ToList());
    }

    public async Task<Result<MemberOrderDto>> GetMyOrderAsync(
        Guid tenantId, Guid identityUserId, Guid orderId, CancellationToken ct = default)
    {
        var member = await FindMemberByIdentityAsync(tenantId, identityUserId, ct);
        if (member == null)
            return FailOrder("Member account not linked / الحساب غير مرتبط بعضو");

        var order = await _db.MemberOrders.AsNoTracking()
            .Include(o => o.Lines)
            .Include(o => o.Member)
            .FirstOrDefaultAsync(o =>
                o.Id == orderId && o.TenantId == tenantId && o.MemberId == member.Id, ct);

        if (order == null)
            return FailOrder("Order not found / الطلب غير موجود");

        return Result<MemberOrderDto>.Success(MapOrder(order));
    }

    public async Task<Result<List<MemberOrderListItemDto>>> ListOrdersForStaffAsync(
        Guid tenantId, string? status = null, CancellationToken ct = default)
    {
        var query = _db.MemberOrders.AsNoTracking()
            .Include(o => o.Lines)
            .Include(o => o.Member)
            .Where(o => o.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToLowerInvariant();
            if (!MemberOrderStatuses.All.Contains(s))
                return Result<List<MemberOrderListItemDto>>.Failure("Invalid status / حالة غير صالحة");
            query = query.Where(o => o.Status == s);
        }

        var rows = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);

        return Result<List<MemberOrderListItemDto>>.Success(rows.Select(MapListItem).ToList());
    }

    public async Task<Result<MemberOrderDto>> GetOrderForStaffAsync(
        Guid tenantId, Guid orderId, CancellationToken ct = default)
    {
        var order = await _db.MemberOrders.AsNoTracking()
            .Include(o => o.Lines)
            .Include(o => o.Member)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == tenantId, ct);

        if (order == null)
            return FailOrder("Order not found / الطلب غير موجود");

        return Result<MemberOrderDto>.Success(MapOrder(order));
    }

    public Task<Result<MemberOrderDto>> AcceptAsync(
        Guid tenantId, Guid orderId, Guid identityUserId, CancellationToken ct = default)
        => TransitionAsync(tenantId, orderId, identityUserId, MemberOrderStatuses.Pending, MemberOrderStatuses.Accepted, ct);

    public async Task<Result<MemberOrderDto>> RejectAsync(
        Guid tenantId, Guid orderId, Guid identityUserId, RejectMemberOrderRequest request, CancellationToken ct = default)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId, ct);
        if (staff == null)
            return FailOrder("Staff user not found / المستخدم غير موجود");

        var order = await _db.MemberOrders
            .Include(o => o.Lines)
            .Include(o => o.Member)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == tenantId, ct);

        if (order == null)
            return FailOrder("Order not found / الطلب غير موجود");

        if (!string.Equals(order.Status, MemberOrderStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            return FailOrder($"Cannot reject from status '{order.Status}' / لا يمكن الرفض من الحالة الحالية");

        order.Status = MemberOrderStatuses.Rejected;
        order.RejectedAtUtc = DateTime.UtcNow;
        order.RejectedByUserId = staff.Id;
        order.RejectionReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        order.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await AuditTransitionAsync(order, "member_order.reject", tenantId);
        await NotifyStatusAsync(order, ct);

        return Result<MemberOrderDto>.Success(MapOrder(order));
    }

    public Task<Result<MemberOrderDto>> MarkReadyAsync(
        Guid tenantId, Guid orderId, Guid identityUserId, CancellationToken ct = default)
        => TransitionAsync(tenantId, orderId, identityUserId, MemberOrderStatuses.Accepted, MemberOrderStatuses.Ready, ct);

    public Task<Result<MemberOrderDto>> CompleteAsync(
        Guid tenantId, Guid orderId, Guid identityUserId, CancellationToken ct = default)
        => TransitionAsync(tenantId, orderId, identityUserId, MemberOrderStatuses.Ready, MemberOrderStatuses.Completed, ct);

    private async Task<Result<MemberOrderDto>> TransitionAsync(
        Guid tenantId,
        Guid orderId,
        Guid identityUserId,
        string fromStatus,
        string toStatus,
        CancellationToken ct)
    {
        var staff = await ResolveAppUserAsync(tenantId, identityUserId, ct);
        if (staff == null)
            return FailOrder("Staff user not found / المستخدم غير موجود");

        var order = await _db.MemberOrders
            .Include(o => o.Lines)
            .Include(o => o.Member)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == tenantId, ct);

        if (order == null)
            return FailOrder("Order not found / الطلب غير موجود");

        if (!string.Equals(order.Status, fromStatus, StringComparison.OrdinalIgnoreCase))
            return FailOrder(
                $"Cannot move to '{toStatus}' from '{order.Status}' / لا يمكن الانتقال إلى {toStatus} من الحالة الحالية");

        order.Status = toStatus;
        order.UpdatedAtUtc = DateTime.UtcNow;
        var now = DateTime.UtcNow;

        switch (toStatus)
        {
            case MemberOrderStatuses.Accepted:
                order.AcceptedAtUtc = now;
                order.AcceptedByUserId = staff.Id;
                break;
            case MemberOrderStatuses.Ready:
                order.ReadyAtUtc = now;
                order.ReadyByUserId = staff.Id;
                break;
            case MemberOrderStatuses.Completed:
                order.CompletedAtUtc = now;
                order.CompletedByUserId = staff.Id;
                break;
        }

        await _db.SaveChangesAsync(ct);
        await AuditTransitionAsync(order, $"member_order.{toStatus}", tenantId);
        await NotifyStatusAsync(order, ct);

        return Result<MemberOrderDto>.Success(MapOrder(order));
    }

    private async Task AuditTransitionAsync(MemberOrder order, string action, Guid tenantId)
    {
        await _audit.LogAsync(
            action,
            "MemberOrder",
            order.Id,
            null,
            new { order.OrderNumber, order.Status, order.RejectionReason },
            tenantIdOverride: tenantId);
    }

    private Task NotifyStatusAsync(MemberOrder order, CancellationToken ct)
        => _notifier.NotifyStatusChangedAsync(
            order.TenantId, order.Id, order.OrderNumber, order.Status, order.MemberId, ct);

    private async Task<string> NextOrderNumberAsync(Guid tenantId, CancellationToken ct)
    {
        var day = DateTime.UtcNow.ToString("yyyyMMdd");
        var prefix = $"MO-{day}-";
        var count = await _db.MemberOrders
            .IgnoreQueryFilters()
            .CountAsync(o => o.TenantId == tenantId && o.OrderNumber.StartsWith(prefix), ct);
        return $"{prefix}{(count + 1):D4}";
    }

    private async Task<Warehouse?> ResolveDefaultWarehouseAsync(Guid tenantId, CancellationToken ct)
    {
        return await _db.Warehouses.AsNoTracking()
                   .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.IsDefault && w.IsActive, ct)
               ?? await _db.Warehouses.AsNoTracking()
                   .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.IsActive, ct);
    }

    private async Task<GymMember?> FindMemberByIdentityAsync(
        Guid tenantId, Guid identityUserId, CancellationToken ct)
    {
        var identityId = identityUserId.ToString();
        return await _db.GymMembers
            .FirstOrDefaultAsync(m =>
                m.TenantId == tenantId
                && m.AppUser != null
                && m.AppUser.UserId == identityId, ct);
    }

    private async Task<AppUser?> ResolveAppUserAsync(
        Guid tenantId, Guid identityUserId, CancellationToken ct)
    {
        var identityId = identityUserId.ToString();
        return await _db.AppUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == identityId, ct);
    }

    private async Task<MemberOrderDto> MapOrderAsync(Guid orderId, Guid tenantId, CancellationToken ct)
    {
        var order = await _db.MemberOrders.AsNoTracking()
            .Include(o => o.Lines)
            .Include(o => o.Member)
            .FirstAsync(o => o.Id == orderId && o.TenantId == tenantId, ct);
        return MapOrder(order);
    }

    private static MemberOrderDto MapOrder(MemberOrder o) => new()
    {
        Id = o.Id,
        OrderNumber = o.OrderNumber,
        Status = o.Status,
        MemberId = o.MemberId,
        MemberName = o.Member?.FullName,
        MemberNumber = o.Member?.MemberNumber,
        WarehouseId = o.WarehouseId,
        Currency = o.Currency,
        Subtotal = o.Subtotal,
        Total = o.Total,
        MemberNotes = o.MemberNotes,
        RejectionReason = o.RejectionReason,
        CreatedAtUtc = o.CreatedAtUtc,
        AcceptedAtUtc = o.AcceptedAtUtc,
        ReadyAtUtc = o.ReadyAtUtc,
        CompletedAtUtc = o.CompletedAtUtc,
        RejectedAtUtc = o.RejectedAtUtc,
        Lines = o.Lines
            .OrderBy(l => l.CreatedAtUtc)
            .Select(l => new MemberOrderLineDto
            {
                Id = l.Id,
                ProductId = l.ProductId,
                ProductSku = l.ProductSku,
                ProductName = l.ProductName,
                ProductNameAr = l.ProductNameAr,
                UnitPrice = l.UnitPrice,
                Qty = l.Qty,
                LineTotal = l.LineTotal,
                Currency = l.Currency
            })
            .ToList()
    };

    private static MemberOrderListItemDto MapListItem(MemberOrder o) => new()
    {
        Id = o.Id,
        OrderNumber = o.OrderNumber,
        Status = o.Status,
        MemberId = o.MemberId,
        MemberName = o.Member?.FullName,
        MemberNumber = o.Member?.MemberNumber,
        Total = o.Total,
        Currency = o.Currency,
        LineCount = o.Lines?.Count ?? 0,
        CreatedAtUtc = o.CreatedAtUtc
    };

    private static Result<MemberOrderDto> FailOrder(string error)
        => Result<MemberOrderDto>.Failure(error);
}
