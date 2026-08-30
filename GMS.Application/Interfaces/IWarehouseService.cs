namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Core.Entities;

public interface IWarehouseService
{
    Task<Result<List<WarehouseDto>>> ListAsync(Guid tenantId, bool includeInactive = false);
    Task<Result<WarehouseDto>> GetAsync(Guid tenantId, Guid id);
    Task<Result<Warehouse?>> GetDefaultAsync(Guid tenantId);
    /// <summary>
    /// Idempotent resolver: returns the tenant's default warehouse, promoting an existing
    /// warehouse or creating a system "Main Stock" warehouse when none exists.
    /// Never returns null for a valid tenant.
    /// </summary>
    Task<Result<Warehouse>> GetOrCreateDefaultAsync(Guid tenantId);
    Task<Result<WarehouseDto>> CreateAsync(Guid tenantId, CreateWarehouseRequest request);
    Task<Result<WarehouseDto>> UpdateAsync(Guid tenantId, Guid id, UpdateWarehouseRequest request);
    Task<Result<WarehouseDto>> SetDefaultAsync(Guid tenantId, Guid id);
}
