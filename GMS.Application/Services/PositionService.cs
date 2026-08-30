namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public class PositionService : IPositionService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<PositionService> _logger;

    public PositionService(GymFlowProDbContext db, IAuditService audit, ILogger<PositionService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<List<PositionDto>>> ListAsync(Guid tenantId, bool includeInactive = false, Guid? departmentId = null)
    {
        var q = _db.Positions.AsNoTracking().Where(p => p.TenantId == tenantId);
        if (!includeInactive)
            q = q.Where(p => p.IsActive);
        if (departmentId.HasValue)
            q = q.Where(p => p.DepartmentId == departmentId);

        var rows = await q.OrderBy(p => p.Name).ToListAsync();
        var departmentNames = await _db.Departments.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .ToDictionaryAsync(d => d.Id, d => d.Name);
        var employeeCounts = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.PositionId != null)
            .GroupBy(e => e.PositionId!.Value)
            .Select(g => new { PositionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PositionId, g => g.Count);

        return Result<List<PositionDto>>.Success(rows.Select(p => Map(p, departmentNames, employeeCounts)).ToList());
    }

    public async Task<Result<PositionDto>> GetAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.Positions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (entity == null)
            return Result<PositionDto>.Failure("Position not found / المسمى الوظيفي غير موجود");

        var departmentName = entity.DepartmentId.HasValue
            ? await _db.Departments.AsNoTracking().Where(d => d.Id == entity.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync()
            : null;
        var employeeCount = await _db.Employees.CountAsync(e => e.TenantId == tenantId && e.PositionId == id);

        return Result<PositionDto>.Success(Map(entity, departmentName, employeeCount));
    }

    public async Task<Result<PositionDto>> CreateAsync(Guid tenantId, CreatePositionRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
            return Result<PositionDto>.Failure("Name is required / الاسم مطلوب");

        if (request.DepartmentId.HasValue)
        {
            var departmentExists = await _db.Departments.AnyAsync(d => d.Id == request.DepartmentId && d.TenantId == tenantId);
            if (!departmentExists)
                return Result<PositionDto>.Failure("Department not found / القسم غير موجود");
        }

        if (request.DefaultBasicSalary is < 0)
            return Result<PositionDto>.Failure("Default basic salary cannot be negative / الراتب الأساسي لا يمكن أن يكون سالباً");

        var entity = new Position
        {
            TenantId = tenantId,
            Name = name,
            NameAr = string.IsNullOrWhiteSpace(request.NameAr) ? null : request.NameAr.Trim(),
            DepartmentId = request.DepartmentId,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            DefaultBasicSalary = request.DefaultBasicSalary,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Positions.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("position.create", "Position", entity.Id, null, new { entity.Name, entity.DepartmentId });
        _logger.LogInformation("Position {Name} created for tenant {TenantId}", entity.Name, tenantId);

        return await GetAsync(tenantId, entity.Id);
    }

    public async Task<Result<PositionDto>> UpdateAsync(Guid tenantId, Guid id, UpdatePositionRequest request)
    {
        var entity = await _db.Positions.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (entity == null)
            return Result<PositionDto>.Failure("Position not found / المسمى الوظيفي غير موجود");

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
            return Result<PositionDto>.Failure("Name is required / الاسم مطلوب");

        if (request.DepartmentId.HasValue)
        {
            var departmentExists = await _db.Departments.AnyAsync(d => d.Id == request.DepartmentId && d.TenantId == tenantId);
            if (!departmentExists)
                return Result<PositionDto>.Failure("Department not found / القسم غير موجود");
        }

        if (request.DefaultBasicSalary is < 0)
            return Result<PositionDto>.Failure("Default basic salary cannot be negative / الراتب الأساسي لا يمكن أن يكون سالباً");

        entity.Name = name;
        entity.NameAr = string.IsNullOrWhiteSpace(request.NameAr) ? null : request.NameAr.Trim();
        entity.DepartmentId = request.DepartmentId;
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.DefaultBasicSalary = request.DefaultBasicSalary;
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("position.update", "Position", entity.Id, null, new { entity.Name, entity.IsActive });

        return await GetAsync(tenantId, entity.Id);
    }

    private static PositionDto Map(Position p, IReadOnlyDictionary<Guid, string> departmentNames, IReadOnlyDictionary<Guid, int> counts) =>
        Map(p,
            p.DepartmentId.HasValue && departmentNames.TryGetValue(p.DepartmentId.Value, out var name) ? name : null,
            counts.TryGetValue(p.Id, out var count) ? count : 0);

    private static PositionDto Map(Position p, string? departmentName, int employeeCount) => new()
    {
        Id = p.Id,
        Name = p.Name,
        NameAr = p.NameAr,
        DepartmentId = p.DepartmentId,
        DepartmentName = departmentName,
        Description = p.Description,
        DefaultBasicSalary = p.DefaultBasicSalary,
        IsActive = p.IsActive,
        EmployeeCount = employeeCount,
        CreatedAtUtc = p.CreatedAtUtc
    };
}
