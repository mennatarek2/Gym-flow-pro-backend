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
/// Payroll Lite lifecycle: Draft -> Calculate -> Approve -> Close. Calculation is only allowed in
/// Draft/Calculated (so adjustments can be added and the period recalculated freely before it's
/// finalized); Approve and Close are one-way gates — once Approved, a period can no longer be
/// recalculated, and PayrollLine values are frozen for good at Close. This is a deliberately more
/// conservative reading than "only Closed is immutable": re-running Calculate after Approve would
/// silently change numbers someone already signed off on, so it's blocked too. Basic salary comes
/// from IEmployeeService.GetContractAsOfAsync, evaluated as of the *period's* last day (not "today") —
/// so calculating, say, a future-dated period correctly picks up a contract that starts mid-period,
/// and a later contract change never rewrites an already-calculated historical line. Lines are built
/// for Active and Suspended employees, plus anyone (e.g. Terminated) who has adjustments or an
/// existing line for the period so final-pay corrections still calculate. Overtime money is a documented practical
/// simplification: EmployeeAttendance.OvertimeMinutes for the month, converted at BasicSalary/240
/// (a standard full-time month, not a real labor-law overtime multiplier) — Payroll Lite explicitly
/// does not implement Egyptian overtime/tax/social-insurance rules.
/// </summary>
public class PayrollPeriodService : IPayrollPeriodService
{
    private const decimal StandardMonthlyHours = 240m;

    private readonly GymFlowProDbContext _db;
    private readonly IEmployeeService _employees;
    private readonly IAuditService _audit;
    private readonly ILogger<PayrollPeriodService> _logger;

    public PayrollPeriodService(
        GymFlowProDbContext db, IEmployeeService employees, IAuditService audit, ILogger<PayrollPeriodService> logger)
    {
        _db = db;
        _employees = employees;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<List<PayrollPeriodDto>>> ListAsync(Guid tenantId)
    {
        var periods = await _db.PayrollPeriods.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .ToListAsync();

        var result = new List<PayrollPeriodDto>();
        foreach (var period in periods)
            result.Add(await MapPeriodAsync(period));

        return Result<List<PayrollPeriodDto>>.Success(result);
    }

    public async Task<Result<PayrollPeriodDto>> GetAsync(Guid tenantId, Guid id)
    {
        var period = await _db.PayrollPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (period == null)
            return Result<PayrollPeriodDto>.Failure("Payroll period not found / فترة الرواتب غير موجودة");

        return Result<PayrollPeriodDto>.Success(await MapPeriodAsync(period));
    }

    public async Task<Result<PayrollPeriodDto>> CreateAsync(Guid tenantId, CreatePayrollPeriodRequest request)
    {
        if (request.Month < 1 || request.Month > 12)
            return Result<PayrollPeriodDto>.Failure("Month must be between 1 and 12 / الشهر يجب أن يكون بين 1 و12");
        if (request.Year < 2000 || request.Year > 2100)
            return Result<PayrollPeriodDto>.Failure("Invalid year / سنة غير صالحة");

        var exists = await _db.PayrollPeriods.AnyAsync(p => p.TenantId == tenantId && p.Year == request.Year && p.Month == request.Month);
        if (exists)
            return Result<PayrollPeriodDto>.Failure("A payroll period already exists for this month / يوجد بالفعل فترة رواتب لهذا الشهر");

        var entity = new PayrollPeriod
        {
            TenantId = tenantId,
            Year = request.Year,
            Month = request.Month,
            Status = PayrollPeriodStatuses.Draft,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.PayrollPeriods.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("payroll_period.create", "PayrollPeriod", entity.Id, null, new { entity.Year, entity.Month });

        return Result<PayrollPeriodDto>.Success(await MapPeriodAsync(entity));
    }

    public async Task<Result<PayrollPeriodDto>> CalculateAsync(Guid tenantId, Guid id)
    {
        var period = await _db.PayrollPeriods.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (period == null)
            return Result<PayrollPeriodDto>.Failure("Payroll period not found / فترة الرواتب غير موجودة");
        if (period.Status != PayrollPeriodStatuses.Draft && period.Status != PayrollPeriodStatuses.Calculated)
            return Result<PayrollPeriodDto>.Failure(
                "Only a Draft or Calculated period can be (re)calculated — this one is already Approved/Closed / يمكن حساب فترة في حالة مسودة أو محسوبة فقط");

        var monthStart = new DateOnly(period.Year, period.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var overtimeByEmployee = await _db.EmployeeAttendances.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AttendanceDate >= monthStart && a.AttendanceDate <= monthEnd)
            .GroupBy(a => a.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, TotalOvertimeMinutes = g.Sum(a => a.OvertimeMinutes) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.TotalOvertimeMinutes);

        var adjustments = await _db.PayrollAdjustments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.PayrollPeriodId == id)
            .ToListAsync();
        var adjustmentsByEmployee = adjustments.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var existingLines = await _db.PayrollLines
            .Where(l => l.TenantId == tenantId && l.PayrollPeriodId == id)
            .ToDictionaryAsync(l => l.EmployeeId);

        // Active + Suspended always. Terminated (and any other status) still get a line when they
        // have adjustments or an existing line for this period — final pay / corrections.
        var extraEmployeeIds = adjustmentsByEmployee.Keys
            .Concat(existingLines.Keys)
            .Distinct()
            .ToHashSet();

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && (
                e.Status == EmployeeStatuses.Active
                || e.Status == EmployeeStatuses.Suspended
                || extraEmployeeIds.Contains(e.Id)))
            .ToListAsync();

        foreach (var employee in employees)
        {
            var contractResult = await _employees.GetContractAsOfAsync(tenantId, employee.Id, monthEnd);
            var basicSalary = contractResult.IsSuccess ? (contractResult.Data?.BasicSalary ?? 0m) : 0m;
            var contractId = contractResult.IsSuccess ? contractResult.Data?.Id : null;

            var overtimeMinutes = overtimeByEmployee.GetValueOrDefault(employee.Id, 0);
            var hourlyRate = basicSalary / StandardMonthlyHours;
            var autoOvertimeAmount = Math.Round((overtimeMinutes / 60m) * hourlyRate, 2, MidpointRounding.AwayFromZero);

            var employeeAdjustments = adjustmentsByEmployee.GetValueOrDefault(employee.Id, new List<PayrollAdjustment>());
            var bonusAmount = SumType(employeeAdjustments, PayrollAdjustmentTypes.Bonus);
            var allowanceAmount = SumType(employeeAdjustments, PayrollAdjustmentTypes.Allowance);
            var deductionAmount = SumType(employeeAdjustments, PayrollAdjustmentTypes.Deduction);
            var manualOvertimeAmount = SumType(employeeAdjustments, PayrollAdjustmentTypes.Overtime);
            var overtimeAmount = autoOvertimeAmount + manualOvertimeAmount;
            var netSalary = basicSalary + overtimeAmount + bonusAmount + allowanceAmount - deductionAmount;

            if (existingLines.TryGetValue(employee.Id, out var line))
            {
                line.ContractId = contractId;
                line.BasicSalary = basicSalary;
                line.OvertimeAmount = overtimeAmount;
                line.BonusAmount = bonusAmount;
                line.AllowanceAmount = allowanceAmount;
                line.DeductionAmount = deductionAmount;
                line.NetSalary = netSalary;
                line.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                _db.PayrollLines.Add(new PayrollLine
                {
                    TenantId = tenantId,
                    PayrollPeriodId = id,
                    EmployeeId = employee.Id,
                    ContractId = contractId,
                    BasicSalary = basicSalary,
                    OvertimeAmount = overtimeAmount,
                    BonusAmount = bonusAmount,
                    AllowanceAmount = allowanceAmount,
                    DeductionAmount = deductionAmount,
                    NetSalary = netSalary,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        period.Status = PayrollPeriodStatuses.Calculated;
        period.CalculatedAtUtc = DateTime.UtcNow;
        period.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("payroll_period.calculate", "PayrollPeriod", period.Id, null,
            new { period.Year, period.Month, employeeCount = employees.Count });
        _logger.LogInformation("Payroll period {Year}-{Month} calculated for tenant {TenantId}: {Count} lines",
            period.Year, period.Month, tenantId, employees.Count);

        return Result<PayrollPeriodDto>.Success(await MapPeriodAsync(period));
    }

    public async Task<Result<PayrollPeriodDto>> ApproveAsync(Guid tenantId, Guid id, Guid? actorAppUserId)
    {
        var period = await _db.PayrollPeriods.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (period == null)
            return Result<PayrollPeriodDto>.Failure("Payroll period not found / فترة الرواتب غير موجودة");
        if (period.Status != PayrollPeriodStatuses.Calculated)
            return Result<PayrollPeriodDto>.Failure("Only a Calculated period can be approved / يمكن اعتماد فترة محسوبة فقط");

        period.Status = PayrollPeriodStatuses.Approved;
        period.ApprovedByAppUserId = actorAppUserId;
        period.ApprovedAtUtc = DateTime.UtcNow;
        period.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("payroll_period.approve", "PayrollPeriod", period.Id, null,
            new { period.Year, period.Month, actorAppUserId });

        return Result<PayrollPeriodDto>.Success(await MapPeriodAsync(period));
    }

    public async Task<Result<PayrollPeriodDto>> CloseAsync(Guid tenantId, Guid id, Guid? actorAppUserId)
    {
        var period = await _db.PayrollPeriods.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (period == null)
            return Result<PayrollPeriodDto>.Failure("Payroll period not found / فترة الرواتب غير موجودة");
        if (period.Status != PayrollPeriodStatuses.Approved)
            return Result<PayrollPeriodDto>.Failure("Only an Approved period can be closed / يمكن إغلاق فترة معتمدة فقط");

        period.Status = PayrollPeriodStatuses.Closed;
        period.ClosedAtUtc = DateTime.UtcNow;
        period.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("payroll_period.close", "PayrollPeriod", period.Id, null,
            new { period.Year, period.Month, actorAppUserId });
        _logger.LogInformation("Payroll period {Year}-{Month} closed for tenant {TenantId}", period.Year, period.Month, tenantId);

        return Result<PayrollPeriodDto>.Success(await MapPeriodAsync(period));
    }

    public async Task<Result<List<PayrollLineDto>>> ListLinesAsync(Guid tenantId, Guid periodId, Guid? employeeId = null)
    {
        var period = await _db.PayrollPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.TenantId == tenantId);
        if (period == null)
            return Result<List<PayrollLineDto>>.Failure("Payroll period not found / فترة الرواتب غير موجودة");

        var q = _db.PayrollLines.AsNoTracking().Where(l => l.TenantId == tenantId && l.PayrollPeriodId == periodId);
        if (employeeId.HasValue)
            q = q.Where(l => l.EmployeeId == employeeId);

        var lines = await q.ToListAsync();
        var employees = await _db.Employees.AsNoTracking().Where(e => e.TenantId == tenantId).ToDictionaryAsync(e => e.Id);

        return Result<List<PayrollLineDto>>.Success(
            lines.Select(l => Map(l, period, employees.GetValueOrDefault(l.EmployeeId))).ToList());
    }

    public async Task<Result<List<PayrollLineDto>>> ListLinesForEmployeeAsync(Guid tenantId, Guid employeeId, int? year = null)
    {
        var q = _db.PayrollLines.AsNoTracking().Where(l => l.TenantId == tenantId && l.EmployeeId == employeeId);
        if (year.HasValue)
            q = q.Where(l => l.PayrollPeriod!.Year == year);

        var lines = await q.Include(l => l.PayrollPeriod).OrderByDescending(l => l.PayrollPeriod!.Year).ThenByDescending(l => l.PayrollPeriod!.Month).ToListAsync();
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId);

        return Result<List<PayrollLineDto>>.Success(lines.Select(l => Map(l, l.PayrollPeriod!, employee)).ToList());
    }

    private static decimal SumType(List<PayrollAdjustment> adjustments, string type) =>
        adjustments.Where(a => string.Equals(a.Type, type, StringComparison.OrdinalIgnoreCase)).Sum(a => a.Amount);

    private async Task<PayrollPeriodDto> MapPeriodAsync(PayrollPeriod period)
    {
        var lines = await _db.PayrollLines.AsNoTracking()
            .Where(l => l.TenantId == period.TenantId && l.PayrollPeriodId == period.Id)
            .ToListAsync();

        return new PayrollPeriodDto
        {
            Id = period.Id,
            Year = period.Year,
            Month = period.Month,
            Status = period.Status,
            EmployeeCount = lines.Count,
            GrossTotal = lines.Sum(l => l.BasicSalary + l.OvertimeAmount + l.BonusAmount + l.AllowanceAmount),
            DeductionsTotal = lines.Sum(l => l.DeductionAmount),
            NetTotal = lines.Sum(l => l.NetSalary),
            CalculatedAtUtc = period.CalculatedAtUtc,
            ApprovedByAppUserId = period.ApprovedByAppUserId,
            ApprovedAtUtc = period.ApprovedAtUtc,
            ClosedAtUtc = period.ClosedAtUtc
        };
    }

    private static PayrollLineDto Map(PayrollLine l, PayrollPeriod period, Employee? employee) => new()
    {
        Id = l.Id,
        PayrollPeriodId = l.PayrollPeriodId,
        Year = period.Year,
        Month = period.Month,
        EmployeeId = l.EmployeeId,
        EmployeeName = employee != null ? $"{employee.FirstName} {employee.LastName}" : string.Empty,
        EmployeeNumber = employee?.EmployeeNumber ?? string.Empty,
        BasicSalary = l.BasicSalary,
        OvertimeAmount = l.OvertimeAmount,
        BonusAmount = l.BonusAmount,
        AllowanceAmount = l.AllowanceAmount,
        DeductionAmount = l.DeductionAmount,
        NetSalary = l.NetSalary,
        PeriodStatus = period.Status
    };
}
