namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

/// <summary>INVS-2 warehouses. BranchId is optional future mapping — no Branch table.</summary>
public class WarehouseService : IWarehouseService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<WarehouseService> _logger;

    public WarehouseService(
        GymFlowProDbContext db,
        IAuditService audit,
        ILogger<WarehouseService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<List<WarehouseDto>>> ListAsync(Guid tenantId, bool includeInactive = false)
    {
        var q = _db.Warehouses.AsNoTracking().Where(w => w.TenantId == tenantId);
        if (!includeInactive)
            q = q.Where(w => w.IsActive);

        var rows = await q
            .OrderByDescending(w => w.IsDefault)
            .ThenBy(w => w.Name)
            .ToListAsync();

        return Result<List<WarehouseDto>>.Success(rows.Select(Map).ToList());
    }

    public async Task<Result<WarehouseDto>> GetAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId);
        if (entity == null)
            return Result<WarehouseDto>.Failure("Warehouse not found / المخزن غير موجود");
        return Result<WarehouseDto>.Success(Map(entity));
    }

    public async Task<Result<Warehouse?>> GetDefaultAsync(Guid tenantId)
    {
        var entity = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.IsDefault && w.IsActive);
        return Result<Warehouse?>.Success(entity);
    }

    /// <inheritdoc />
    public async Task<Result<Warehouse>> GetOrCreateDefaultAsync(Guid tenantId)
    {
        // 1. Existing default
        var existing = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.IsDefault && w.IsActive);
        if (existing != null)
            return Result<Warehouse>.Success(existing);

        // 2. Promote any active warehouse
        var candidate = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.IsActive);
        if (candidate != null)
        {
            candidate.IsDefault = true;
            candidate.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _logger.LogInformation(
                "Promoted warehouse {Code} to default for tenant {TenantId}",
                candidate.Code, tenantId);
            return Result<Warehouse>.Success(candidate);
        }

        // 3. Create system default
        var entity = new Warehouse
        {
            TenantId = tenantId,
            Code = "MAIN",
            Name = "Main Stock",
            IsDefault = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Warehouses.Add(entity);
        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "Created system default warehouse MAIN for tenant {TenantId}", tenantId);
        return Result<Warehouse>.Success(entity);
    }

    public async Task<Result<WarehouseDto>> CreateAsync(Guid tenantId, CreateWarehouseRequest request)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var exists = await _db.Warehouses.AnyAsync(w => w.TenantId == tenantId && w.Code == code);
        if (exists)
            return Result<WarehouseDto>.Failure("Warehouse code already exists / كود المخزن مستخدم بالفعل");

        var anyExisting = await _db.Warehouses.AnyAsync(w => w.TenantId == tenantId);
        var makeDefault = request.IsDefault || !anyExisting;

        if (makeDefault)
            await ClearDefaultAsync(tenantId);

        var entity = new Warehouse
        {
            TenantId = tenantId,
            Code = code,
            Name = request.Name.Trim(),
            NameAr = string.IsNullOrWhiteSpace(request.NameAr) ? null : request.NameAr.Trim(),
            IsDefault = makeDefault,
            IsActive = request.IsActive,
            BranchId = request.BranchId,
            CreatedAtUtc = DateTime.UtcNow
        };

        // First warehouse must be active + default
        if (!anyExisting)
        {
            entity.IsActive = true;
            entity.IsDefault = true;
        }

        if (entity.IsDefault && !entity.IsActive)
            return Result<WarehouseDto>.Failure(
                "Default warehouse must be active / المخزن الافتراضي يجب أن يكون نشطاً");

        _db.Warehouses.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("warehouse.create", "Warehouse", entity.Id, null, new { entity.Code, entity.Name, entity.IsDefault });
        _logger.LogInformation("Warehouse {Code} created for tenant {TenantId} default={Default}",
            entity.Code, tenantId, entity.IsDefault);

        return Result<WarehouseDto>.Success(Map(entity));
    }

    public async Task<Result<WarehouseDto>> UpdateAsync(
        Guid tenantId, Guid id, UpdateWarehouseRequest request)
    {
        var entity = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId);
        if (entity == null)
            return Result<WarehouseDto>.Failure("Warehouse not found / المخزن غير موجود");

        // Code immutable after stock movements (INVS-3+). Hook reserved — always immutable for now except never exposed on update.

        if (!request.IsActive)
        {
            if (entity.IsDefault)
                return Result<WarehouseDto>.Failure(
                    "Cannot deactivate the default warehouse — set another default first / لا يمكن تعطيل المخزن الافتراضي قبل تعيين افتراضي آخر");

            var otherActive = await _db.Warehouses.AnyAsync(w =>
                w.TenantId == tenantId && w.Id != id && w.IsActive);
            if (!otherActive)
                return Result<WarehouseDto>.Failure(
                    "Cannot deactivate the only active warehouse / لا يمكن تعطيل المخزن النشط الوحيد");
        }

        entity.Name = request.Name.Trim();
        entity.NameAr = string.IsNullOrWhiteSpace(request.NameAr) ? null : request.NameAr.Trim();
        entity.IsActive = request.IsActive;
        entity.BranchId = request.BranchId;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("warehouse.update", "Warehouse", entity.Id, null, new { entity.Code, entity.Name, entity.IsActive });
        return Result<WarehouseDto>.Success(Map(entity));
    }

    public async Task<Result<WarehouseDto>> SetDefaultAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId);
        if (entity == null)
            return Result<WarehouseDto>.Failure("Warehouse not found / المخزن غير موجود");

        if (!entity.IsActive)
            return Result<WarehouseDto>.Failure(
                "Cannot set an inactive warehouse as default / لا يمكن تعيين مخزن غير نشط كافتراضي");

        await ClearDefaultAsync(tenantId);
        entity.IsDefault = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("warehouse.set_default", "Warehouse", entity.Id, null, new { entity.Code });

        return Result<WarehouseDto>.Success(Map(entity));
    }

    private async Task ClearDefaultAsync(Guid tenantId)
    {
        var currents = await _db.Warehouses
            .Where(w => w.TenantId == tenantId && w.IsDefault)
            .ToListAsync();
        foreach (var w in currents)
        {
            w.IsDefault = false;
            w.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static WarehouseDto Map(Warehouse w) => new()
    {
        Id = w.Id,
        Code = w.Code,
        Name = w.Name,
        NameAr = w.NameAr,
        IsDefault = w.IsDefault,
        IsActive = w.IsActive,
        BranchId = w.BranchId,
        CreatedAtUtc = w.CreatedAtUtc,
        UpdatedAtUtc = w.UpdatedAtUtc
    };
}
