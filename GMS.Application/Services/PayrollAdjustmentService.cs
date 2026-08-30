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
/// Manual payroll inputs (Bonus/Allowance/Overtime/Deduction). Only creatable while the parent
/// period is Draft/Calculated — PayrollPeriodService.CalculateAsync sums these into the next
/// recalculation; once Approved/Closed the period is frozen and no new adjustment can affect it.
/// </summary>
public class PayrollAdjustmentService : IPayrollAdjustmentService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<PayrollAdjustmentService> _logger;

    public PayrollAdjustmentService(GymFlowProDbContext db, IAuditService audit, ILogger<PayrollAdjustmentService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<List<PayrollAdjustmentDto>>> ListAsync(Guid tenantId, Guid periodId, Guid? employeeId = null)
    {
        var q = _db.PayrollAdjustments.AsNoTracking().Where(a => a.TenantId == tenantId && a.PayrollPeriodId == periodId);
        if (employeeId.HasValue)
            q = q.Where(a => a.EmployeeId == employeeId);

        var rows = await q.OrderByDescending(a => a.CreatedAtUtc).ToListAsync();
        return Result<List<PayrollAdjustmentDto>>.Success(rows.Select(Map).ToList());
    }

    public async Task<Result<PayrollAdjustmentDto>> CreateAsync(
        Guid tenantId, Guid periodId, CreatePayrollAdjustmentRequest request, Guid? actorAppUserId)
    {
        var period = await _db.PayrollPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.TenantId == tenantId);
        if (period == null)
            return Result<PayrollAdjustmentDto>.Failure("Payroll period not found / فترة الرواتب غير موجودة");
        if (period.Status != PayrollPeriodStatuses.Draft && period.Status != PayrollPeriodStatuses.Calculated)
            return Result<PayrollAdjustmentDto>.Failure(
                "Adjustments can only be added to a Draft or Calculated period — this one is already Approved/Closed / يمكن إضافة التعديلات فقط لفترة في حالة مسودة أو محسوبة");

        var employeeExists = await _db.Employees.AnyAsync(e => e.Id == request.EmployeeId && e.TenantId == tenantId);
        if (!employeeExists)
            return Result<PayrollAdjustmentDto>.Failure("Employee not found / الموظف غير موجود");

        var type = request.Type?.Trim() ?? string.Empty;
        if (!PayrollAdjustmentTypes.All.Contains(type))
            return Result<PayrollAdjustmentDto>.Failure("Invalid adjustment type / نوع التعديل غير صالح");

        if (request.Amount <= 0)
            return Result<PayrollAdjustmentDto>.Failure("Amount must be greater than zero / المبلغ يجب أن يكون أكبر من صفر");

        var entity = new PayrollAdjustment
        {
            TenantId = tenantId,
            PayrollPeriodId = periodId,
            EmployeeId = request.EmployeeId,
            Type = type,
            Amount = request.Amount,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            CreatedByAppUserId = actorAppUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.PayrollAdjustments.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("payroll_adjustment.create", "PayrollAdjustment", entity.Id, null,
            new { entity.EmployeeId, entity.Type, entity.Amount, entity.Reason });
        _logger.LogInformation("Payroll adjustment {Type} {Amount} created for employee {EmployeeId} in period {PeriodId}",
            type, request.Amount, request.EmployeeId, periodId);

        return Result<PayrollAdjustmentDto>.Success(Map(entity));
    }

    private static PayrollAdjustmentDto Map(PayrollAdjustment a) => new()
    {
        Id = a.Id,
        PayrollPeriodId = a.PayrollPeriodId,
        EmployeeId = a.EmployeeId,
        Type = a.Type,
        Amount = a.Amount,
        Reason = a.Reason,
        CreatedAtUtc = a.CreatedAtUtc
    };
}
