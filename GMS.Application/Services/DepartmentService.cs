namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public class DepartmentService : IDepartmentService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<DepartmentService> _logger;

    public DepartmentService(GymFlowProDbContext db, IAuditService audit, ILogger<DepartmentService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<List<DepartmentDto>>> ListAsync(Guid tenantId, bool includeInactive = false)
    {
        var q = _db.Departments.AsNoTracking().Where(d => d.TenantId == tenantId);
        if (!includeInactive)
            q = q.Where(d => d.IsActive);

        var rows = await q.OrderBy(d => d.Name).ToListAsync();
        var employeeCounts = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.DepartmentId != null)
            .GroupBy(e => e.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.DepartmentId, g => g.Count);

        return Result<List<DepartmentDto>>.Success(rows.Select(d => Map(d, employeeCounts)).ToList());
    }

    public async Task<Result<DepartmentDto>> GetAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.Departments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId);
        if (entity == null)
            return Result<DepartmentDto>.Failure("Department not found / القسم غير موجود");

        var employeeCount = await _db.Employees.CountAsync(e => e.TenantId == tenantId && e.DepartmentId == id);
        return Result<DepartmentDto>.Success(Map(entity, employeeCount));
    }

    public async Task<Result<DepartmentDto>> CreateAsync(Guid tenantId, CreateDepartmentRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
            return Result<DepartmentDto>.Failure("Name is required / الاسم مطلوب");

        var exists = await _db.Departments.AnyAsync(d => d.TenantId == tenantId && d.Name == name);
        if (exists)
            return Result<DepartmentDto>.Failure("Department name already exists / اسم القسم مستخدم بالفعل");

        var entity = new Department
        {
            TenantId = tenantId,
            Name = name,
            NameAr = string.IsNullOrWhiteSpace(request.NameAr) ? null : request.NameAr.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Departments.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("department.create", "Department", entity.Id, null, new { entity.Name });
        _logger.LogInformation("Department {Name} created for tenant {TenantId}", entity.Name, tenantId);

        return Result<DepartmentDto>.Success(Map(entity, 0));
    }

    public async Task<Result<DepartmentDto>> UpdateAsync(Guid tenantId, Guid id, UpdateDepartmentRequest request)
    {
        var entity = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId);
        if (entity == null)
            return Result<DepartmentDto>.Failure("Department not found / القسم غير موجود");

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
            return Result<DepartmentDto>.Failure("Name is required / الاسم مطلوب");

        var duplicate = await _db.Departments.AnyAsync(d => d.TenantId == tenantId && d.Id != id && d.Name == name);
        if (duplicate)
            return Result<DepartmentDto>.Failure("Department name already exists / اسم القسم مستخدم بالفعل");

        entity.Name = name;
        entity.NameAr = string.IsNullOrWhiteSpace(request.NameAr) ? null : request.NameAr.Trim();
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("department.update", "Department", entity.Id, null, new { entity.Name, entity.IsActive });

        var employeeCount = await _db.Employees.CountAsync(e => e.TenantId == tenantId && e.DepartmentId == id);
        return Result<DepartmentDto>.Success(Map(entity, employeeCount));
    }

    private static DepartmentDto Map(Department d, IReadOnlyDictionary<Guid, int> counts) =>
        Map(d, counts.TryGetValue(d.Id, out var count) ? count : 0);

    private static DepartmentDto Map(Department d, int employeeCount) => new()
    {
        Id = d.Id,
        Name = d.Name,
        NameAr = d.NameAr,
        IsActive = d.IsActive,
        EmployeeCount = employeeCount,
        CreatedAtUtc = d.CreatedAtUtc
    };
}
