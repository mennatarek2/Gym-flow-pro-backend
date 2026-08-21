namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public class SupplierService : ISupplierService
{
    public const int LedgerTakeCap = 500;

    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;

    public SupplierService(GymFlowProDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<List<SupplierDto>>> ListAsync(
        Guid tenantId, bool includeInactive = false, bool includeMoney = false)
    {
        var q = _db.Suppliers.AsNoTracking().Where(s => s.TenantId == tenantId);
        if (!includeInactive) q = q.Where(s => s.IsActive);
        var rows = await q.OrderBy(s => s.Name).ToListAsync();
        Dictionary<Guid, MoneyAgg>? money = null;
        if (includeMoney && rows.Count > 0)
            money = await AggregateMoneyAsync(tenantId, rows.Select(r => r.Id).ToList());
        return Result<List<SupplierDto>>.Success(
            rows.Select(s => Map(s, includeMoney ? money?.GetValueOrDefault(s.Id) : null)).ToList());
    }

    public async Task<Result<SupplierDto>> GetAsync(Guid tenantId, Guid id, bool includeMoney = false)
    {
        var s = await _db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (s == null) return Result<SupplierDto>.Failure("Supplier not found / المورد غير موجود");
        MoneyAgg? agg = null;
        if (includeMoney)
        {
            var map = await AggregateMoneyAsync(tenantId, new List<Guid> { id });
            map.TryGetValue(id, out agg);
        }
        return Result<SupplierDto>.Success(Map(s, includeMoney ? agg ?? MoneyAgg.Empty : null));
    }

    public async Task<Result<SupplierDto>> CreateAsync(Guid tenantId, CreateSupplierRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<SupplierDto>.Failure("Name required / الاسم مطلوب");

        var openingCheck = ValidateOpeningOptional(request.OpeningAmount, request.OpeningOwedToSupplier);
        if (openingCheck != null) return Result<SupplierDto>.Failure(openingCheck);

        var entity = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name.Trim(),
            NameAr = NullIfWhite(request.NameAr),
            Phone = NullIfWhite(request.Phone),
            Email = NullIfWhite(request.Email),
            PaymentTerms = NullIfWhite(request.PaymentTerms),
            Notes = NullIfWhite(request.Notes),
            Address = NullIfWhite(request.Address),
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Suppliers.Add(entity);

        if (request.OpeningAmount is > 0)
        {
            var signed = SignedOpening(request.OpeningAmount.Value, request.OpeningOwedToSupplier != false);
            _db.SupplierLedgerEntries.Add(new SupplierLedgerEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SupplierId = entity.Id,
                Amount = signed,
                Reason = SupplierLedgerReasons.Opening,
                Note = "Opening balance",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("supplier.create", "Supplier", entity.Id, null, new { entity.Name });
        var map = await AggregateMoneyAsync(tenantId, new List<Guid> { entity.Id });
        map.TryGetValue(entity.Id, out var agg);
        return Result<SupplierDto>.Success(Map(entity, agg ?? MoneyAgg.Empty));
    }

    public async Task<Result<SupplierDto>> UpdateAsync(Guid tenantId, Guid id, UpdateSupplierRequest request)
    {
        var entity = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        if (entity == null) return Result<SupplierDto>.Failure("Supplier not found / المورد غير موجود");
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<SupplierDto>.Failure("Name required / الاسم مطلوب");

        // Opening on update is ignored — use PostOpeningAsync (reject second opening).
        entity.Name = request.Name.Trim();
        entity.NameAr = NullIfWhite(request.NameAr);
        entity.Phone = NullIfWhite(request.Phone);
        entity.Email = NullIfWhite(request.Email);
        entity.PaymentTerms = NullIfWhite(request.PaymentTerms);
        entity.Notes = NullIfWhite(request.Notes);
        entity.Address = NullIfWhite(request.Address);
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("supplier.update", "Supplier", entity.Id, null, new { entity.Name });
        var map = await AggregateMoneyAsync(tenantId, new List<Guid> { id });
        map.TryGetValue(id, out var agg);
        return Result<SupplierDto>.Success(Map(entity, agg ?? MoneyAgg.Empty));
    }

    public async Task<Result<SupplierBalanceDto>> GetBalanceAsync(Guid tenantId, Guid supplierId)
    {
        var exists = await _db.Suppliers.AsNoTracking()
            .AnyAsync(s => s.Id == supplierId && s.TenantId == tenantId);
        if (!exists) return Result<SupplierBalanceDto>.Failure("Supplier not found / المورد غير موجود");

        var map = await AggregateMoneyAsync(tenantId, new List<Guid> { supplierId });
        var agg = map.GetValueOrDefault(supplierId) ?? MoneyAgg.Empty;
        return Result<SupplierBalanceDto>.Success(new SupplierBalanceDto
        {
            SupplierId = supplierId,
            PurchasesTotal = agg.Purchases,
            PaidTotal = agg.Paid,
            OpeningTotal = agg.Opening,
            DueTotal = agg.Due
        });
    }

    public async Task<Result<InventoryListPageDto<SupplierLedgerEntryDto>>> ListLedgerAsync(
        Guid tenantId, Guid supplierId, DateTime? fromUtc = null, DateTime? toUtc = null)
    {
        var exists = await _db.Suppliers.AsNoTracking()
            .AnyAsync(s => s.Id == supplierId && s.TenantId == tenantId);
        if (!exists)
            return Result<InventoryListPageDto<SupplierLedgerEntryDto>>.Failure(
                "Supplier not found / المورد غير موجود");

        var q = _db.SupplierLedgerEntries.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.SupplierId == supplierId);
        if (fromUtc.HasValue) q = q.Where(e => e.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue) q = q.Where(e => e.CreatedAtUtc <= toUtc.Value);

        var take = LedgerTakeCap;
        var rows = await q.OrderBy(e => e.CreatedAtUtc).ThenBy(e => e.Id)
            .Take(take + 1)
            .ToListAsync();
        var truncated = rows.Count > take;
        if (truncated) rows = rows.Take(take).ToList();

        return Result<InventoryListPageDto<SupplierLedgerEntryDto>>.Success(
            new InventoryListPageDto<SupplierLedgerEntryDto>
            {
                Items = rows.Select(MapEntry).ToList(),
                Truncated = truncated,
                Take = take
            });
    }

    public async Task<Result<SupplierLedgerEntryDto>> PostOpeningAsync(
        Guid tenantId, Guid supplierId, PostSupplierOpeningRequest request)
    {
        var exists = await _db.Suppliers.AsNoTracking()
            .AnyAsync(s => s.Id == supplierId && s.TenantId == tenantId);
        if (!exists) return Result<SupplierLedgerEntryDto>.Failure("Supplier not found / المورد غير موجود");
        if (request.Amount <= 0)
            return Result<SupplierLedgerEntryDto>.Failure("Amount must be > 0 / المبلغ يجب أن يكون أكبر من صفر");

        var hasOpening = await _db.SupplierLedgerEntries.AsNoTracking()
            .AnyAsync(e =>
                e.TenantId == tenantId
                && e.SupplierId == supplierId
                && e.Reason == SupplierLedgerReasons.Opening);
        if (hasOpening)
            return Result<SupplierLedgerEntryDto>.Failure(
                "Opening already posted; use compensating entry / الرصيد الافتتاحي مسجل مسبقاً");

        var entry = new SupplierLedgerEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SupplierId = supplierId,
            Amount = SignedOpening(request.Amount, request.OwedToSupplier),
            Reason = SupplierLedgerReasons.Opening,
            Note = NullIfWhite(request.Note) ?? "Opening balance",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.SupplierLedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("supplier.opening", "Supplier", supplierId, null,
            new { entry.Amount, entry.Id });
        return Result<SupplierLedgerEntryDto>.Success(MapEntry(entry));
    }

    public async Task<Result<SupplierLedgerEntryDto>> PostPaymentAsync(
        Guid tenantId, Guid supplierId, PostSupplierPaymentRequest request)
    {
        var exists = await _db.Suppliers.AsNoTracking()
            .AnyAsync(s => s.Id == supplierId && s.TenantId == tenantId);
        if (!exists) return Result<SupplierLedgerEntryDto>.Failure("Supplier not found / المورد غير موجود");
        if (request.Amount <= 0)
            return Result<SupplierLedgerEntryDto>.Failure("Amount must be > 0 / المبلغ يجب أن يكون أكبر من صفر");

        var noteParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Method))
            noteParts.Add("method=" + request.Method.Trim());
        if (request.PaidAtUtc.HasValue)
            noteParts.Add("paidAt=" + request.PaidAtUtc.Value.ToUniversalTime().ToString("o"));
        if (!string.IsNullOrWhiteSpace(request.Note))
            noteParts.Add(request.Note.Trim());

        var entry = new SupplierLedgerEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SupplierId = supplierId,
            Amount = -Math.Abs(request.Amount),
            Reason = SupplierLedgerReasons.Payment,
            Note = noteParts.Count > 0 ? string.Join("; ", noteParts) : null,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.SupplierLedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("supplier.payment", "Supplier", supplierId, null,
            new { entry.Amount, entry.Id });
        // Payments never post stock — intentional AP-only write.
        return Result<SupplierLedgerEntryDto>.Success(MapEntry(entry));
    }

    private async Task<Dictionary<Guid, MoneyAgg>> AggregateMoneyAsync(Guid tenantId, List<Guid> supplierIds)
    {
        if (supplierIds.Count == 0) return new Dictionary<Guid, MoneyAgg>();
        var rows = await _db.SupplierLedgerEntries.AsNoTracking()
            .Where(e => e.TenantId == tenantId && supplierIds.Contains(e.SupplierId))
            .GroupBy(e => new { e.SupplierId, e.Reason })
            .Select(g => new { g.Key.SupplierId, g.Key.Reason, Sum = g.Sum(x => x.Amount) })
            .ToListAsync();

        var dict = supplierIds.ToDictionary(id => id, _ => new MoneyAgg());
        foreach (var r in rows)
        {
            var agg = dict[r.SupplierId];
            agg.Due += r.Sum;
            if (string.Equals(r.Reason, SupplierLedgerReasons.Purchase, StringComparison.OrdinalIgnoreCase))
                agg.Purchases += r.Sum;
            else if (string.Equals(r.Reason, SupplierLedgerReasons.Payment, StringComparison.OrdinalIgnoreCase))
                agg.Paid += -r.Sum; // payments stored negative
            else if (string.Equals(r.Reason, SupplierLedgerReasons.Opening, StringComparison.OrdinalIgnoreCase))
                agg.Opening += r.Sum;
        }
        return dict;
    }

    private static string? ValidateOpeningOptional(decimal? amount, bool? owedToSupplier)
    {
        if (amount == null || amount == 0) return null;
        if (amount < 0) return "Opening amount must be >= 0 / مبلغ الافتتاحي يجب ألا يكون سالباً";
        _ = owedToSupplier;
        return null;
    }

    private static decimal SignedOpening(decimal absolute, bool owedToSupplier)
        => owedToSupplier ? Math.Abs(absolute) : -Math.Abs(absolute);

    private static string? NullIfWhite(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static SupplierDto Map(Supplier s, MoneyAgg? money) => new()
    {
        Id = s.Id,
        Name = s.Name,
        NameAr = s.NameAr,
        Phone = s.Phone,
        Email = s.Email,
        PaymentTerms = s.PaymentTerms,
        Notes = s.Notes,
        Address = s.Address,
        IsActive = s.IsActive,
        CreatedAtUtc = s.CreatedAtUtc,
        PurchasesTotal = money?.Purchases,
        PaidTotal = money?.Paid,
        DueTotal = money?.Due
    };

    private static SupplierLedgerEntryDto MapEntry(SupplierLedgerEntry e) => new()
    {
        Id = e.Id,
        Amount = e.Amount,
        Reason = e.Reason,
        ReferenceType = e.ReferenceType,
        ReferenceId = e.ReferenceId,
        Note = e.Note,
        CreatedAtUtc = e.CreatedAtUtc
    };

    private sealed class MoneyAgg
    {
        public static MoneyAgg Empty => new();
        public decimal Purchases { get; set; }
        public decimal Paid { get; set; }
        public decimal Opening { get; set; }
        public decimal Due { get; set; }
    }
}
