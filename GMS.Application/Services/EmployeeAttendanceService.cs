namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

public class EmployeeAttendanceService : IEmployeeAttendanceService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<EmployeeAttendanceService> _logger;

    public EmployeeAttendanceService(GymFlowProDbContext db, IAuditService audit, ILogger<EmployeeAttendanceService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<EmployeeAttendanceDto>> CheckInAsync(
        Guid tenantId, Guid employeeId, string? notes, string source, Guid? actorAppUserId)
    {
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (employee == null)
            return Result<EmployeeAttendanceDto>.Failure("Employee not found / الموظف غير موجود");

        var today = MembershipOperational.TodayCairo();

        var row = await _db.EmployeeAttendances
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EmployeeId == employeeId && a.AttendanceDate == today);
        if (row?.CheckInAtUtc != null)
            return Result<EmployeeAttendanceDto>.Failure("Already checked in today / تم تسجيل الحضور بالفعل اليوم");

        var schedule = await _db.EmployeeScheduleAssignments.AsNoTracking()
            .Include(a => a.EmployeeShift)
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EmployeeId == employeeId && a.Date == today);

        var checkInAtUtc = DateTime.UtcNow;
        var (lateMinutes, status) = AttendanceCalculator.ComputeCheckIn(
            checkInAtUtc, today, schedule?.EmployeeShift?.StartTime, schedule?.EmployeeShift?.GraceMinutes ?? 0);

        if (row == null)
        {
            row = new EmployeeAttendance
            {
                TenantId = tenantId,
                EmployeeId = employeeId,
                AttendanceDate = today,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.EmployeeAttendances.Add(row);
        }

        row.ScheduleId = schedule?.Id;
        row.CheckInAtUtc = checkInAtUtc;
        row.LateMinutes = lateMinutes;
        row.Status = status;
        row.Source = source;
        row.Notes = string.IsNullOrWhiteSpace(notes) ? row.Notes : notes.Trim();
        row.CreatedByAppUserId = actorAppUserId;
        row.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee_attendance.check_in", "EmployeeAttendance", row.Id, null,
            new { row.EmployeeId, row.AttendanceDate, row.CheckInAtUtc, row.LateMinutes, row.Status, row.Source });

        return Result<EmployeeAttendanceDto>.Success(await MapAsync(row));
    }

    public async Task<Result<EmployeeAttendanceDto>> CheckOutAsync(Guid tenantId, Guid employeeId, Guid? actorAppUserId)
    {
        var today = MembershipOperational.TodayCairo();

        var row = await _db.EmployeeAttendances
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EmployeeId == employeeId && a.AttendanceDate == today);
        if (row?.CheckInAtUtc == null)
            return Result<EmployeeAttendanceDto>.Failure("Cannot check out without checking in first / لا يمكن تسجيل الانصراف قبل تسجيل الحضور");
        if (row.CheckOutAtUtc != null)
            return Result<EmployeeAttendanceDto>.Failure("Already checked out today / تم تسجيل الانصراف بالفعل اليوم");

        TimeOnly? shiftStart = null, shiftEnd = null;
        if (row.ScheduleId.HasValue)
        {
            var shift = await _db.EmployeeScheduleAssignments.AsNoTracking()
                .Include(a => a.EmployeeShift)
                .Where(a => a.Id == row.ScheduleId)
                .Select(a => a.EmployeeShift)
                .FirstOrDefaultAsync();
            shiftStart = shift?.StartTime;
            shiftEnd = shift?.EndTime;
        }

        var checkOutAtUtc = DateTime.UtcNow;
        var calc = AttendanceCalculator.ComputeCheckOut(row.CheckInAtUtc.Value, checkOutAtUtc, row.AttendanceDate, shiftStart, shiftEnd);
        if (!calc.IsSuccess)
            return Result<EmployeeAttendanceDto>.Failure(calc.Error!);

        row.CheckOutAtUtc = checkOutAtUtc;
        row.WorkedMinutes = calc.Data.WorkedMinutes;
        row.OvertimeMinutes = calc.Data.OvertimeMinutes;
        row.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee_attendance.check_out", "EmployeeAttendance", row.Id, null,
            new { row.EmployeeId, row.AttendanceDate, row.CheckOutAtUtc, row.WorkedMinutes, row.OvertimeMinutes });

        return Result<EmployeeAttendanceDto>.Success(await MapAsync(row));
    }

    public async Task<Result<List<EmployeeAttendanceDto>>> ListAsync(
        Guid tenantId, DateOnly from, DateOnly to, Guid? employeeId = null, string? status = null)
    {
        var q = _db.EmployeeAttendances.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AttendanceDate >= from && a.AttendanceDate <= to);
        if (employeeId.HasValue)
            q = q.Where(a => a.EmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(a => a.Status == status);

        var rows = await q.OrderByDescending(a => a.AttendanceDate).ToListAsync();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .ToDictionaryAsync(e => e.Id);
        var scheduleIds = rows.Where(r => r.ScheduleId.HasValue).Select(r => r.ScheduleId!.Value).Distinct().ToList();
        var shiftNames = await _db.EmployeeScheduleAssignments.AsNoTracking()
            .Where(a => scheduleIds.Contains(a.Id))
            .Select(a => new { a.Id, ShiftName = a.EmployeeShift!.Name })
            .ToDictionaryAsync(a => a.Id, a => a.ShiftName);

        return Result<List<EmployeeAttendanceDto>>.Success(
            rows.Select(r => Map(r, employees.GetValueOrDefault(r.EmployeeId), r.ScheduleId.HasValue ? shiftNames.GetValueOrDefault(r.ScheduleId.Value) : null)).ToList());
    }

    public async Task<Result<EmployeeAttendanceDto>> CorrectAsync(
        Guid tenantId, Guid attendanceId, CorrectAttendanceRequest request, Guid? actorAppUserId)
    {
        var row = await _db.EmployeeAttendances.FirstOrDefaultAsync(a => a.Id == attendanceId && a.TenantId == tenantId);
        if (row == null)
            return Result<EmployeeAttendanceDto>.Failure("Attendance record not found / سجل الحضور غير موجود");

        var before = new { row.Status, row.CheckInAtUtc, row.CheckOutAtUtc, row.WorkedMinutes, row.Notes };

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!AttendanceStatuses.All.Contains(request.Status))
                return Result<EmployeeAttendanceDto>.Failure("Invalid status / حالة غير صالحة");
            row.Status = request.Status;
        }

        if (request.CheckInAtUtc.HasValue)
            row.CheckInAtUtc = request.CheckInAtUtc;
        if (request.CheckOutAtUtc.HasValue)
            row.CheckOutAtUtc = request.CheckOutAtUtc;

        if (row.CheckInAtUtc.HasValue && row.CheckOutAtUtc.HasValue)
        {
            if (row.CheckOutAtUtc <= row.CheckInAtUtc)
                return Result<EmployeeAttendanceDto>.Failure("Check-out must be after check-in / وقت الانصراف يجب أن يكون بعد وقت الحضور");
            row.WorkedMinutes = (int)Math.Round((row.CheckOutAtUtc.Value - row.CheckInAtUtc.Value).TotalMinutes, MidpointRounding.AwayFromZero);
        }

        if (request.Notes != null)
            row.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        row.CreatedByAppUserId ??= actorAppUserId;
        row.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee_attendance.correct", "EmployeeAttendance", row.Id, before,
            new { row.Status, row.CheckInAtUtc, row.CheckOutAtUtc, row.WorkedMinutes, row.Notes });

        return Result<EmployeeAttendanceDto>.Success(await MapAsync(row));
    }

    public async Task<Guid?> ResolveEmployeeIdForCallerAsync(Guid tenantId, Guid identityUserId)
    {
        var appUserId = await ResolveAppUserIdForCallerAsync(tenantId, identityUserId);
        if (appUserId == null)
            return null;

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e =>
                e.TenantId == tenantId
                && (e.EmployeeAppUserId == appUserId || e.AppUserId == appUserId));

        if (employee == null)
            return null;

        if (!string.Equals(employee.Status, EmployeeStatuses.Active, StringComparison.OrdinalIgnoreCase))
            return null;

        return employee.Id;
    }

    public async Task<Guid?> ResolveAppUserIdForCallerAsync(Guid tenantId, Guid identityUserId)
    {
        var identityIdStr = identityUserId.ToString();
        var appUser = await _db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == identityIdStr && u.TenantId == tenantId);
        return appUser?.Id;
    }

    private async Task<EmployeeAttendanceDto> MapAsync(EmployeeAttendance row)
    {
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == row.EmployeeId);
        string? shiftName = null;
        if (row.ScheduleId.HasValue)
        {
            shiftName = await _db.EmployeeScheduleAssignments.AsNoTracking()
                .Where(a => a.Id == row.ScheduleId)
                .Select(a => a.EmployeeShift!.Name)
                .FirstOrDefaultAsync();
        }
        return Map(row, employee, shiftName);
    }

    private static EmployeeAttendanceDto Map(EmployeeAttendance a, Employee? employee, string? shiftName) => new()
    {
        Id = a.Id,
        EmployeeId = a.EmployeeId,
        EmployeeName = employee != null ? $"{employee.FirstName} {employee.LastName}" : string.Empty,
        EmployeeNumber = employee?.EmployeeNumber ?? string.Empty,
        ScheduleId = a.ScheduleId,
        EmployeeShiftName = shiftName,
        AttendanceDate = a.AttendanceDate,
        CheckInAtUtc = a.CheckInAtUtc,
        CheckOutAtUtc = a.CheckOutAtUtc,
        WorkedMinutes = a.WorkedMinutes,
        LateMinutes = a.LateMinutes,
        OvertimeMinutes = a.OvertimeMinutes,
        Status = a.Status,
        Source = a.Source,
        Notes = a.Notes
    };
}
