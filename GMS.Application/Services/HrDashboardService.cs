namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Every metric here is a live query against real HR data — no cached/fabricated numbers. "Absent
/// today" is necessarily a derived, live count (scheduled today, no check-in, not on leave) since
/// Phase 3 deliberately has no end-of-day absence-marking job (see EmployeeAttendanceService).
/// </summary>
public class HrDashboardService : IHrDashboardService
{
    private readonly GymFlowProDbContext _db;

    public HrDashboardService(GymFlowProDbContext db)
    {
        _db = db;
    }

    public async Task<Result<HrDashboardDto>> GetAsync(Guid tenantId, bool includePayroll)
    {
        var today = MembershipOperational.TodayCairo();
        var soon = today.AddDays(30);

        var employeeCount = await _db.Employees.CountAsync(e => e.TenantId == tenantId && e.Status == EmployeeStatuses.Active);

        var todaysAttendance = await _db.EmployeeAttendances.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AttendanceDate == today)
            .ToListAsync();
        var presentToday = todaysAttendance.Count(a => a.CheckInAtUtc != null);
        var lateToday = todaysAttendance.Count(a => a.Status == AttendanceStatuses.Late);
        var overtimeMinutesToday = todaysAttendance.Sum(a => a.OvertimeMinutes);

        var onLeaveToday = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.Status == LeaveRequestStatuses.Approved
                && l.StartDate <= today && l.EndDate >= today)
            .Select(l => l.EmployeeId)
            .Distinct()
            .CountAsync();

        var scheduledTodayEmployeeIds = await _db.EmployeeScheduleAssignments.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Date == today)
            .Select(s => s.EmployeeId)
            .Distinct()
            .ToListAsync();
        var checkedInTodayEmployeeIds = todaysAttendance.Where(a => a.CheckInAtUtc != null).Select(a => a.EmployeeId).ToHashSet();
        var onLeaveTodayEmployeeIds = (await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.Status == LeaveRequestStatuses.Approved && l.StartDate <= today && l.EndDate >= today)
            .Select(l => l.EmployeeId).ToListAsync()).ToHashSet();
        var absentToday = scheduledTodayEmployeeIds.Count(id => !checkedInTodayEmployeeIds.Contains(id) && !onLeaveTodayEmployeeIds.Contains(id));

        var pendingLeaveRequests = await _db.LeaveRequests.CountAsync(l => l.TenantId == tenantId && l.Status == LeaveRequestStatuses.Pending);

        var upcomingContractExpirations = await _db.EmployeeContracts.CountAsync(c =>
            c.TenantId == tenantId && c.Status == ContractStatuses.Active && c.EndDate != null && c.EndDate >= today && c.EndDate <= soon);

        var expiringDocuments = await _db.EmployeeDocuments.CountAsync(d =>
            d.TenantId == tenantId && d.ExpiryDate != null && d.ExpiryDate >= today && d.ExpiryDate <= soon);
        var expiredDocuments = await _db.EmployeeDocuments.CountAsync(d =>
            d.TenantId == tenantId && d.ExpiryDate != null && d.ExpiryDate < today);

        decimal? payrollNet = null;
        string? payrollStatus = null;
        if (includePayroll)
        {
            var currentPeriod = await _db.PayrollPeriods.AsNoTracking()
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Year == today.Year && p.Month == today.Month);
            if (currentPeriod != null)
            {
                payrollStatus = currentPeriod.Status;
                payrollNet = await _db.PayrollLines.AsNoTracking()
                    .Where(l => l.TenantId == tenantId && l.PayrollPeriodId == currentPeriod.Id)
                    .SumAsync(l => (decimal?)l.NetSalary) ?? 0m;
            }
        }

        return Result<HrDashboardDto>.Success(new HrDashboardDto
        {
            EmployeeCount = employeeCount,
            PresentToday = presentToday,
            LateToday = lateToday,
            AbsentToday = absentToday,
            OnLeaveToday = onLeaveToday,
            OvertimeMinutesToday = overtimeMinutesToday,
            PayrollNetThisMonth = payrollNet,
            PayrollStatusThisMonth = payrollStatus,
            PendingLeaveRequests = pendingLeaveRequests,
            UpcomingContractExpirations = upcomingContractExpirations,
            ExpiringDocuments = expiringDocuments,
            ExpiredDocuments = expiredDocuments
        });
    }
}
