namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public class EmployeeScheduleService : IEmployeeScheduleService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<EmployeeScheduleService> _logger;

    public EmployeeScheduleService(GymFlowProDbContext db, IAuditService audit, ILogger<EmployeeScheduleService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<EmployeeScheduleAssignmentDto>> AssignAsync(Guid tenantId, AssignScheduleRequest request)
    {
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.TenantId == tenantId);
        if (employee == null)
            return Result<EmployeeScheduleAssignmentDto>.Failure("Employee not found / الموظف غير موجود");

        var shift = await _db.EmployeeShifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.EmployeeShiftId && s.TenantId == tenantId);
        if (shift == null)
            return Result<EmployeeScheduleAssignmentDto>.Failure("Shift template not found / قالب الوردية غير موجود");

        var existing = await _db.EmployeeScheduleAssignments.AsNoTracking()
            .Include(a => a.EmployeeShift)
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EmployeeId == request.EmployeeId && a.Date == request.Date);
        if (existing != null)
            return Result<EmployeeScheduleAssignmentDto>.Failure(
                $"Already assigned to {existing.EmployeeShift?.Name} on {request.Date:yyyy-MM-dd} — remove it first / "
                + "معيّن بالفعل لوردية أخرى في هذا التاريخ — قم بإزالتها أولاً");

        var entity = new EmployeeScheduleAssignment
        {
            TenantId = tenantId,
            EmployeeId = request.EmployeeId,
            EmployeeShiftId = request.EmployeeShiftId,
            Date = request.Date,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.EmployeeScheduleAssignments.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee_schedule.assign", "EmployeeScheduleAssignment", entity.Id, null,
            new { entity.EmployeeId, entity.EmployeeShiftId, entity.Date });

        return Result<EmployeeScheduleAssignmentDto>.Success(Map(employee, shift, entity));
    }

    public async Task<Result<bool>> RemoveAsync(Guid tenantId, Guid employeeId, DateOnly date)
    {
        var entity = await _db.EmployeeScheduleAssignments
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EmployeeId == employeeId && a.Date == date);
        if (entity == null)
            return Result<bool>.Failure("Schedule assignment not found / لا يوجد تعيين وردية لهذا اليوم");

        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee_schedule.remove", "EmployeeScheduleAssignment", entity.Id, null,
            new { entity.EmployeeId, entity.Date });

        return Result<bool>.Success(true);
    }

    public async Task<Result<List<EmployeeScheduleAssignmentDto>>> ListAsync(
        Guid tenantId, DateOnly from, DateOnly to, Guid? employeeId = null)
    {
        var q = _db.EmployeeScheduleAssignments.AsNoTracking()
            .Include(a => a.Employee)
            .Include(a => a.EmployeeShift)
            .Where(a => a.TenantId == tenantId && a.Date >= from && a.Date <= to);
        if (employeeId.HasValue)
            q = q.Where(a => a.EmployeeId == employeeId);

        var rows = await q.OrderBy(a => a.Date).ThenBy(a => a.Employee!.FirstName).ToListAsync();
        return Result<List<EmployeeScheduleAssignmentDto>>.Success(
            rows.Select(a => Map(a.Employee!, a.EmployeeShift!, a)).ToList());
    }

    public async Task<Result<BulkAssignResultDto>> BulkAssignAsync(Guid tenantId, BulkAssignScheduleRequest request)
    {
        if (request.DateTo < request.DateFrom)
            return Result<BulkAssignResultDto>.Failure("End date must be on or after start date / تاريخ الانتهاء يجب أن يكون بعد تاريخ البدء");
        if (request.EmployeeIds.Count == 0)
            return Result<BulkAssignResultDto>.Failure("Select at least one employee / اختر موظفاً واحداً على الأقل");

        var shift = await _db.EmployeeShifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.EmployeeShiftId && s.TenantId == tenantId);
        if (shift == null)
            return Result<BulkAssignResultDto>.Failure("Shift template not found / قالب الوردية غير موجود");

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && request.EmployeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        var existingDates = await _db.EmployeeScheduleAssignments.AsNoTracking()
            .Where(a => a.TenantId == tenantId
                && request.EmployeeIds.Contains(a.EmployeeId)
                && a.Date >= request.DateFrom && a.Date <= request.DateTo)
            .Select(a => new { a.EmployeeId, a.Date })
            .ToListAsync();
        var existingSet = existingDates.Select(a => (a.EmployeeId, a.Date)).ToHashSet();

        var result = new BulkAssignResultDto();
        var toInsert = new List<EmployeeScheduleAssignment>();

        foreach (var employeeId in request.EmployeeIds.Distinct())
        {
            if (!employees.ContainsKey(employeeId))
            {
                result.Cells.Add(new BulkAssignResultCellDto { EmployeeId = employeeId, Date = request.DateFrom, Success = false, SkipReason = "Employee not found / الموظف غير موجود" });
                result.SkippedCount++;
                continue;
            }

            for (var date = request.DateFrom; date <= request.DateTo; date = date.AddDays(1))
            {
                if (existingSet.Contains((employeeId, date)))
                {
                    result.Cells.Add(new BulkAssignResultCellDto { EmployeeId = employeeId, Date = date, Success = false, SkipReason = "Already assigned / معيّن بالفعل" });
                    result.SkippedCount++;
                    continue;
                }

                toInsert.Add(new EmployeeScheduleAssignment
                {
                    TenantId = tenantId,
                    EmployeeId = employeeId,
                    EmployeeShiftId = request.EmployeeShiftId,
                    Date = date,
                    Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                    CreatedAtUtc = DateTime.UtcNow
                });
                result.Cells.Add(new BulkAssignResultCellDto { EmployeeId = employeeId, Date = date, Success = true });
                result.AssignedCount++;
            }
        }

        if (toInsert.Count > 0)
        {
            _db.EmployeeScheduleAssignments.AddRange(toInsert);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("employee_schedule.assign", "EmployeeScheduleAssignment", null, null,
                new { bulk = true, request.EmployeeShiftId, request.DateFrom, request.DateTo, count = toInsert.Count });
        }

        _logger.LogInformation("Bulk schedule assign for tenant {TenantId}: {Assigned} assigned, {Skipped} skipped",
            tenantId, result.AssignedCount, result.SkippedCount);

        return Result<BulkAssignResultDto>.Success(result);
    }

    private static EmployeeScheduleAssignmentDto Map(Employee employee, EmployeeShift shift, EmployeeScheduleAssignment a) => new()
    {
        Id = a.Id,
        EmployeeId = employee.Id,
        EmployeeName = $"{employee.FirstName} {employee.LastName}",
        EmployeeShiftId = shift.Id,
        EmployeeShiftName = shift.Name,
        ShiftStartTime = shift.StartTime,
        ShiftEndTime = shift.EndTime,
        Date = a.Date,
        Notes = a.Notes
    };
}
