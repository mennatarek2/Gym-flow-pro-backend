namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Leave request lifecycle + its EmployeeAttendance integration. Approving a whole-day leave type
/// (everything except Permission, which is a fractional-day errand, not a day off) writes/updates
/// EmployeeAttendance rows to Status=OnLeave for each covered date — but only for rows that don't
/// already have a real CheckInAtUtc, so a genuine recorded attendance is never silently overwritten.
/// Cancelling a previously-approved leave removes exactly the OnLeave rows it created (tracked via
/// EmployeeAttendance.LeaveRequestId) and restores the consumed balance.
/// </summary>
public class LeaveRequestService : ILeaveRequestService
{
    private readonly GymFlowProDbContext _db;
    private readonly ILeaveBalanceService _balances;
    private readonly IAuditService _audit;
    private readonly ILogger<LeaveRequestService> _logger;

    public LeaveRequestService(
        GymFlowProDbContext db, ILeaveBalanceService balances, IAuditService audit, ILogger<LeaveRequestService> logger)
    {
        _db = db;
        _balances = balances;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<List<LeaveRequestDto>>> ListAsync(
        Guid tenantId, Guid? employeeId = null, string? status = null, DateOnly? from = null, DateOnly? to = null)
    {
        var q = _db.LeaveRequests.AsNoTracking().Where(l => l.TenantId == tenantId);
        if (employeeId.HasValue)
            q = q.Where(l => l.EmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(l => l.Status == status);
        if (from.HasValue)
            q = q.Where(l => l.EndDate >= from);
        if (to.HasValue)
            q = q.Where(l => l.StartDate <= to);

        var rows = await q.OrderByDescending(l => l.RequestedAtUtc).ToListAsync();
        var employees = await _db.Employees.AsNoTracking().Where(e => e.TenantId == tenantId).ToDictionaryAsync(e => e.Id);

        return Result<List<LeaveRequestDto>>.Success(rows.Select(l => Map(l, employees.GetValueOrDefault(l.EmployeeId))).ToList());
    }

    public async Task<Result<LeaveRequestDto>> GetAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId);
        if (entity == null)
            return Result<LeaveRequestDto>.Failure("Leave request not found / طلب الإجازة غير موجود");

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entity.EmployeeId);
        return Result<LeaveRequestDto>.Success(Map(entity, employee));
    }

    public async Task<Result<LeaveRequestDto>> CreateAsync(Guid tenantId, Guid employeeId, CreateLeaveRequestRequest request)
    {
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (employee == null)
            return Result<LeaveRequestDto>.Failure("Employee not found / الموظف غير موجود");

        var leaveType = request.LeaveType?.Trim() ?? string.Empty;
        if (!LeaveTypes.All.Contains(leaveType))
            return Result<LeaveRequestDto>.Failure("Invalid leave type / نوع الإجازة غير صالح");

        if (request.EndDate < request.StartDate)
            return Result<LeaveRequestDto>.Failure("Start date cannot be after end date / تاريخ البدء لا يمكن أن يكون بعد تاريخ الانتهاء");

        decimal durationDays;
        if (string.Equals(leaveType, LeaveTypes.Permission, StringComparison.OrdinalIgnoreCase))
        {
            if (request.StartDate != request.EndDate)
                return Result<LeaveRequestDto>.Failure("Permission leave must be a single day / إذن الانصراف يجب أن يكون ليوم واحد");
            durationDays = request.DurationDays ?? 0.25m;
            if (durationDays <= 0 || durationDays > 1)
                return Result<LeaveRequestDto>.Failure("Permission duration must be between 0 and 1 day / مدة الإذن يجب أن تكون بين 0 و1 يوم");
        }
        else
        {
            durationDays = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
        }

        var overlapsApproved = await HasOverlappingApprovedLeaveAsync(tenantId, employeeId, request.StartDate, request.EndDate, excludeId: null);
        if (overlapsApproved)
            return Result<LeaveRequestDto>.Failure(
                "This date range overlaps an already-approved leave for this employee / هذه الفترة تتداخل مع إجازة معتمدة بالفعل لهذا الموظف");

        var entity = new LeaveRequest
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            LeaveType = leaveType,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            DurationDays = durationDays,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            Status = LeaveRequestStatuses.Pending,
            RequestedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.LeaveRequests.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("leave_request.create", "LeaveRequest", entity.Id, null,
            new { entity.EmployeeId, entity.LeaveType, entity.StartDate, entity.EndDate, entity.DurationDays });

        return Result<LeaveRequestDto>.Success(Map(entity, employee));
    }

    public async Task<Result<LeaveRequestDto>> ApproveAsync(Guid tenantId, Guid id, Guid? reviewerAppUserId, string? notes)
    {
        var entity = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId);
        if (entity == null)
            return Result<LeaveRequestDto>.Failure("Leave request not found / طلب الإجازة غير موجود");
        if (entity.Status != LeaveRequestStatuses.Pending)
            return Result<LeaveRequestDto>.Failure("Only pending requests can be approved / يمكن اعتماد الطلبات المعلقة فقط");

        var overlapsApproved = await HasOverlappingApprovedLeaveAsync(tenantId, entity.EmployeeId, entity.StartDate, entity.EndDate, excludeId: entity.Id);
        if (overlapsApproved)
            return Result<LeaveRequestDto>.Failure(
                "This date range now overlaps another approved leave for this employee / هذه الفترة تتداخل الآن مع إجازة معتمدة أخرى لهذا الموظف");

        if (LeaveTypes.TracksBalance(entity.LeaveType))
        {
            var balanceResult = await _balances.GetOrCreateBalanceAsync(tenantId, entity.EmployeeId, entity.LeaveType, entity.StartDate.Year);
            if (!balanceResult.IsSuccess)
                return Result<LeaveRequestDto>.Failure(balanceResult.Error!);
            balanceResult.Data!.UsedDays += entity.DurationDays;
            balanceResult.Data.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (!string.Equals(entity.LeaveType, LeaveTypes.Permission, StringComparison.OrdinalIgnoreCase))
            await ApplyOnLeaveAttendanceAsync(tenantId, entity);

        entity.Status = LeaveRequestStatuses.Approved;
        entity.ReviewedByAppUserId = reviewerAppUserId;
        entity.ReviewedAtUtc = DateTime.UtcNow;
        entity.ReviewNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("leave_request.approve", "LeaveRequest", entity.Id, null,
            new { entity.EmployeeId, entity.LeaveType, entity.StartDate, entity.EndDate, reviewerAppUserId });
        _logger.LogInformation("Leave request {Id} approved for employee {EmployeeId}", entity.Id, entity.EmployeeId);

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entity.EmployeeId);
        return Result<LeaveRequestDto>.Success(Map(entity, employee));
    }

    public async Task<Result<LeaveRequestDto>> RejectAsync(Guid tenantId, Guid id, Guid? reviewerAppUserId, string? notes)
    {
        var entity = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId);
        if (entity == null)
            return Result<LeaveRequestDto>.Failure("Leave request not found / طلب الإجازة غير موجود");
        if (entity.Status != LeaveRequestStatuses.Pending)
            return Result<LeaveRequestDto>.Failure("Only pending requests can be rejected / يمكن رفض الطلبات المعلقة فقط");

        entity.Status = LeaveRequestStatuses.Rejected;
        entity.ReviewedByAppUserId = reviewerAppUserId;
        entity.ReviewedAtUtc = DateTime.UtcNow;
        entity.ReviewNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("leave_request.reject", "LeaveRequest", entity.Id, null,
            new { entity.EmployeeId, entity.LeaveType, reviewerAppUserId, notes });

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entity.EmployeeId);
        return Result<LeaveRequestDto>.Success(Map(entity, employee));
    }

    public async Task<Result<LeaveRequestDto>> CancelAsync(
        Guid tenantId, Guid id, Guid? actorAppUserId, bool isSelfService, Guid? selfEmployeeId)
    {
        var entity = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId);
        if (entity == null)
            return Result<LeaveRequestDto>.Failure("Leave request not found / طلب الإجازة غير موجود");

        if (isSelfService)
        {
            if (entity.EmployeeId != selfEmployeeId)
                return Result<LeaveRequestDto>.Failure("You can only cancel your own leave requests / يمكنك فقط إلغاء طلبات إجازتك الخاصة");
            if (entity.Status != LeaveRequestStatuses.Pending)
                return Result<LeaveRequestDto>.Failure("Only a pending request can be cancelled — ask a manager to cancel an approved one / يمكن إلغاء الطلبات المعلقة فقط — اطلب من المدير إلغاء الطلب المعتمد");
        }
        else if (entity.Status != LeaveRequestStatuses.Pending && entity.Status != LeaveRequestStatuses.Approved)
        {
            return Result<LeaveRequestDto>.Failure("Only pending or approved requests can be cancelled / يمكن إلغاء الطلبات المعلقة أو المعتمدة فقط");
        }

        var wasApproved = entity.Status == LeaveRequestStatuses.Approved;

        if (wasApproved)
        {
            if (LeaveTypes.TracksBalance(entity.LeaveType))
            {
                var balanceResult = await _balances.GetOrCreateBalanceAsync(tenantId, entity.EmployeeId, entity.LeaveType, entity.StartDate.Year);
                if (balanceResult.IsSuccess)
                {
                    balanceResult.Data!.UsedDays = Math.Max(0, balanceResult.Data.UsedDays - entity.DurationDays);
                    balanceResult.Data.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            var leaveDerivedRows = await _db.EmployeeAttendances
                .Where(a => a.TenantId == tenantId && a.LeaveRequestId == entity.Id && a.CheckInAtUtc == null)
                .ToListAsync();
            foreach (var row in leaveDerivedRows)
            {
                row.IsDeleted = true;
                row.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        entity.Status = LeaveRequestStatuses.Cancelled;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("leave_request.cancel", "LeaveRequest", entity.Id, null,
            new { entity.EmployeeId, entity.LeaveType, restoredBalance = wasApproved, actorAppUserId });

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entity.EmployeeId);
        return Result<LeaveRequestDto>.Success(Map(entity, employee));
    }

    private async Task<bool> HasOverlappingApprovedLeaveAsync(Guid tenantId, Guid employeeId, DateOnly start, DateOnly end, Guid? excludeId)
    {
        var q = _db.LeaveRequests.AsNoTracking().Where(l =>
            l.TenantId == tenantId && l.EmployeeId == employeeId && l.Status == LeaveRequestStatuses.Approved
            && l.StartDate <= end && l.EndDate >= start);
        if (excludeId.HasValue)
            q = q.Where(l => l.Id != excludeId);
        return await q.AnyAsync();
    }

    /// <summary>Writes/updates OnLeave attendance rows for every date in the leave range. Restores
    /// soft-deleted placeholder rows (e.g. after a cancelled leave) instead of inserting duplicates —
    /// the unique index is not filtered on IsDeleted. Skips days that already have a real check-in.</summary>
    private async Task ApplyOnLeaveAttendanceAsync(Guid tenantId, LeaveRequest leave)
    {
        var existingRows = await _db.EmployeeAttendances
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.EmployeeId == leave.EmployeeId
                && a.AttendanceDate >= leave.StartDate && a.AttendanceDate <= leave.EndDate)
            .ToListAsync();
        var byDate = existingRows.ToDictionary(a => a.AttendanceDate);

        for (var date = leave.StartDate; date <= leave.EndDate; date = date.AddDays(1))
        {
            if (byDate.TryGetValue(date, out var row))
            {
                if (!row.IsDeleted && row.CheckInAtUtc != null)
                    continue; // real attendance already recorded — never overwrite it

                if (row.IsDeleted)
                {
                    row.IsDeleted = false;
                    row.CheckInAtUtc = null;
                    row.CheckOutAtUtc = null;
                    row.WorkedMinutes = 0;
                    row.LateMinutes = 0;
                    row.OvertimeMinutes = 0;
                    row.ScheduleId = null;
                    row.CreatedByAppUserId = null;
                    row.Notes = null;
                }

                row.Status = AttendanceStatuses.OnLeave;
                row.LeaveRequestId = leave.Id;
                row.Source = AttendanceSources.System;
                row.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                _db.EmployeeAttendances.Add(new EmployeeAttendance
                {
                    TenantId = tenantId,
                    EmployeeId = leave.EmployeeId,
                    AttendanceDate = date,
                    Status = AttendanceStatuses.OnLeave,
                    Source = AttendanceSources.System,
                    LeaveRequestId = leave.Id,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }
    }

    private static LeaveRequestDto Map(LeaveRequest l, Employee? employee) => new()
    {
        Id = l.Id,
        EmployeeId = l.EmployeeId,
        EmployeeName = employee != null ? $"{employee.FirstName} {employee.LastName}" : string.Empty,
        EmployeeNumber = employee?.EmployeeNumber ?? string.Empty,
        LeaveType = l.LeaveType,
        StartDate = l.StartDate,
        EndDate = l.EndDate,
        DurationDays = l.DurationDays,
        Reason = l.Reason,
        Status = l.Status,
        RequestedAtUtc = l.RequestedAtUtc,
        ReviewedByAppUserId = l.ReviewedByAppUserId,
        ReviewedAtUtc = l.ReviewedAtUtc,
        ReviewNotes = l.ReviewNotes
    };
}
