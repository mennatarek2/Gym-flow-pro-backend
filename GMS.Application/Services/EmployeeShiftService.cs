namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public class EmployeeShiftService : IEmployeeShiftService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<EmployeeShiftService> _logger;

    public EmployeeShiftService(GymFlowProDbContext db, IAuditService audit, ILogger<EmployeeShiftService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<List<EmployeeShiftDto>>> ListAsync(Guid tenantId, bool includeInactive = false)
    {
        var q = _db.EmployeeShifts.AsNoTracking().Where(s => s.TenantId == tenantId);
        if (!includeInactive)
            q = q.Where(s => s.IsActive);

        var rows = await q.OrderBy(s => s.StartTime).ThenBy(s => s.Name).ToListAsync();
        return Result<List<EmployeeShiftDto>>.Success(rows.Select(Map).ToList());
    }

    public async Task<Result<EmployeeShiftDto>> GetAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.EmployeeShifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        if (entity == null)
            return Result<EmployeeShiftDto>.Failure("Shift template not found / قالب الوردية غير موجود");
        return Result<EmployeeShiftDto>.Success(Map(entity));
    }

    public async Task<Result<EmployeeShiftDto>> CreateAsync(Guid tenantId, CreateEmployeeShiftRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
            return Result<EmployeeShiftDto>.Failure("Name is required / الاسم مطلوب");

        var validation = ValidateTimes(request.StartTime, request.EndTime, request.BreakMinutes, request.GraceMinutes);
        if (validation != null)
            return Result<EmployeeShiftDto>.Failure(validation);

        var exists = await _db.EmployeeShifts.AnyAsync(s => s.TenantId == tenantId && s.Name == name);
        if (exists)
            return Result<EmployeeShiftDto>.Failure("Shift template name already exists / اسم قالب الوردية مستخدم بالفعل");

        var entity = new EmployeeShift
        {
            TenantId = tenantId,
            Name = name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            BreakMinutes = request.BreakMinutes,
            GraceMinutes = request.GraceMinutes,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.EmployeeShifts.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee_shift.create", "EmployeeShift", entity.Id, null,
            new { entity.Name, entity.StartTime, entity.EndTime, entity.GraceMinutes });
        _logger.LogInformation("Employee shift {Name} created for tenant {TenantId}", entity.Name, tenantId);

        return Result<EmployeeShiftDto>.Success(Map(entity));
    }

    public async Task<Result<EmployeeShiftDto>> UpdateAsync(Guid tenantId, Guid id, UpdateEmployeeShiftRequest request)
    {
        var entity = await _db.EmployeeShifts.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        if (entity == null)
            return Result<EmployeeShiftDto>.Failure("Shift template not found / قالب الوردية غير موجود");

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
            return Result<EmployeeShiftDto>.Failure("Name is required / الاسم مطلوب");

        var validation = ValidateTimes(request.StartTime, request.EndTime, request.BreakMinutes, request.GraceMinutes);
        if (validation != null)
            return Result<EmployeeShiftDto>.Failure(validation);

        var duplicate = await _db.EmployeeShifts.AnyAsync(s => s.TenantId == tenantId && s.Id != id && s.Name == name);
        if (duplicate)
            return Result<EmployeeShiftDto>.Failure("Shift template name already exists / اسم قالب الوردية مستخدم بالفعل");

        entity.Name = name;
        entity.StartTime = request.StartTime;
        entity.EndTime = request.EndTime;
        entity.BreakMinutes = request.BreakMinutes;
        entity.GraceMinutes = request.GraceMinutes;
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee_shift.update", "EmployeeShift", entity.Id, null,
            new { entity.Name, entity.StartTime, entity.EndTime, entity.GraceMinutes, entity.IsActive });

        return Result<EmployeeShiftDto>.Success(Map(entity));
    }

    private static string? ValidateTimes(TimeOnly start, TimeOnly end, int breakMinutes, int graceMinutes)
    {
        if (start == end)
            return "Start and end time cannot be the same / وقت البدء والانتهاء لا يمكن أن يتطابقا";
        if (breakMinutes < 0)
            return "Break minutes cannot be negative / دقائق الاستراحة لا يمكن أن تكون سالبة";
        if (graceMinutes < 0)
            return "Grace minutes cannot be negative / دقائق السماح لا يمكن أن تكون سالبة";
        return null;
    }

    private static EmployeeShiftDto Map(EmployeeShift s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        BreakMinutes = s.BreakMinutes,
        GraceMinutes = s.GraceMinutes,
        IsActive = s.IsActive,
        CrossesMidnight = s.EndTime <= s.StartTime,
        CreatedAtUtc = s.CreatedAtUtc
    };
}
