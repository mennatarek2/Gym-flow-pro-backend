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

/// <summary>
/// Leave balances are auto-provisioned per (employee, leave type, year) on first access using
/// <see cref="LeaveTypes.DefaultEntitlementDays"/> — a practical, tenant-editable starting point,
/// not Egyptian labor law. Unpaid leave never gets a balance row (it never consumes one).
/// </summary>
public class LeaveBalanceService : ILeaveBalanceService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<LeaveBalanceService> _logger;

    public LeaveBalanceService(GymFlowProDbContext db, IAuditService audit, ILogger<LeaveBalanceService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<List<LeaveBalanceDto>>> ListAsync(Guid tenantId, Guid employeeId, int? year = null)
    {
        var employeeExists = await _db.Employees.AnyAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (!employeeExists)
            return Result<List<LeaveBalanceDto>>.Failure("Employee not found / الموظف غير موجود");

        var targetYear = year ?? MembershipOperational.TodayCairo().Year;

        foreach (var leaveType in LeaveTypes.All.Where(LeaveTypes.TracksBalance))
        {
            var ensured = await GetOrCreateBalanceAsync(tenantId, employeeId, leaveType, targetYear);
            if (!ensured.IsSuccess)
                return Result<List<LeaveBalanceDto>>.Failure(ensured.Error!);
        }
        await _db.SaveChangesAsync();

        var rows = await _db.LeaveBalances.AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.EmployeeId == employeeId && b.Year == targetYear)
            .OrderBy(b => b.LeaveType)
            .ToListAsync();

        return Result<List<LeaveBalanceDto>>.Success(rows.Select(Map).ToList());
    }

    public async Task<Result<LeaveBalanceDto>> SetEntitlementAsync(
        Guid tenantId, Guid employeeId, string leaveType, int year, decimal entitledDays)
    {
        if (!LeaveTypes.All.Contains(leaveType))
            return Result<LeaveBalanceDto>.Failure("Invalid leave type / نوع الإجازة غير صالح");
        if (!LeaveTypes.TracksBalance(leaveType))
            return Result<LeaveBalanceDto>.Failure("Unpaid leave has no balance to set / الإجازة بدون أجر ليس لها رصيد");
        if (entitledDays < 0)
            return Result<LeaveBalanceDto>.Failure("Entitled days cannot be negative / أيام الاستحقاق لا يمكن أن تكون سالبة");

        var employeeExists = await _db.Employees.AnyAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (!employeeExists)
            return Result<LeaveBalanceDto>.Failure("Employee not found / الموظف غير موجود");

        var ensured = await GetOrCreateBalanceAsync(tenantId, employeeId, leaveType, year);
        if (!ensured.IsSuccess)
            return Result<LeaveBalanceDto>.Failure(ensured.Error!);
        var balance = ensured.Data!;

        var before = balance.EntitledDays;
        balance.EntitledDays = entitledDays;
        balance.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("leave_balance.set_entitlement", "LeaveBalance", balance.Id, new { entitledDays = before },
            new { entitledDays = balance.EntitledDays, leaveType, year, employeeId });
        _logger.LogInformation("Leave balance {LeaveType}/{Year} for employee {EmployeeId} set to {Days} days",
            leaveType, year, employeeId, entitledDays);

        return Result<LeaveBalanceDto>.Success(Map(balance));
    }

    public async Task<Result<LeaveBalance>> GetOrCreateBalanceAsync(Guid tenantId, Guid employeeId, string leaveType, int year)
    {
        if (!LeaveTypes.TracksBalance(leaveType))
            return Result<LeaveBalance>.Failure("Unpaid leave has no balance / الإجازة بدون أجر ليس لها رصيد");

        var balance = await _db.LeaveBalances
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.EmployeeId == employeeId && b.LeaveType == leaveType && b.Year == year);
        if (balance != null)
            return Result<LeaveBalance>.Success(balance);

        balance = new LeaveBalance
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            LeaveType = leaveType,
            Year = year,
            EntitledDays = LeaveTypes.DefaultEntitlementDays(leaveType),
            UsedDays = 0m,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.LeaveBalances.Add(balance);
        return Result<LeaveBalance>.Success(balance);
    }

    private static LeaveBalanceDto Map(LeaveBalance b) => new()
    {
        Id = b.Id,
        EmployeeId = b.EmployeeId,
        LeaveType = b.LeaveType,
        Year = b.Year,
        EntitledDays = b.EntitledDays,
        UsedDays = b.UsedDays,
        RemainingDays = b.EntitledDays - b.UsedDays
    };
}
