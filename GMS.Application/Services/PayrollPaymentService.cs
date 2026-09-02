namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public sealed class PayrollPaymentService : IPayrollPaymentService
{
    private static readonly HashSet<string> Methods =
        new(StringComparer.OrdinalIgnoreCase) { "cash", "bank_transfer", "wallet", "other" };

    private readonly GymFlowProDbContext _db;

    public PayrollPaymentService(GymFlowProDbContext db)
    {
        _db = db;
    }

    public async Task<Result<PayrollPaymentDto>> CreateAsync(
        Guid tenantId,
        Guid payrollPeriodId,
        Guid identityUserId,
        CreatePayrollPaymentRequest request,
        CancellationToken ct = default)
    {
        if (request.Amount <= 0m)
            return Result<PayrollPaymentDto>.Failure("Payroll payment amount must be greater than zero.");
        if (!Methods.Contains(request.PaymentMethod))
            return Result<PayrollPaymentDto>.Failure("Payroll payment method is invalid.");

        var period = await _db.PayrollPeriods
            .Include(item => item.Lines)
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == payrollPeriodId, ct);
        if (period == null)
            return Result<PayrollPaymentDto>.Failure("Payroll period not found.");
        if (period.Status is not PayrollPeriodStatuses.Approved and not PayrollPeriodStatuses.Closed)
            return Result<PayrollPaymentDto>.Failure("Only approved or closed payroll can be paid.");

        var line = period.Lines.FirstOrDefault(item => item.Id == request.PayrollLineId);
        if (line == null)
            return Result<PayrollPaymentDto>.Failure("Payroll line not found in this period.");
        var alreadyPaid = await _db.PayrollPayments
            .Where(payment => payment.TenantId == tenantId
                && payment.PayrollLineId == request.PayrollLineId
                && payment.Status == "posted")
            .SumAsync(payment => (decimal?)payment.Amount, ct) ?? 0m;
        if (alreadyPaid + request.Amount > line.NetSalary)
            return Result<PayrollPaymentDto>.Failure("Payroll payment exceeds the period liability.");

        var actor = await _db.AppUsers
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenantId
                && user.IsActive
                && (user.Id == identityUserId || user.UserId == identityUserId.ToString()))
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(ct);
        if (!actor.HasValue)
            return Result<PayrollPaymentDto>.Failure("Authenticated staff profile was not found.");

        var expense = new CashExpense
        {
            TenantId = tenantId,
            ExpenseDate = request.PaidDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            Category = "payroll",
            Amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero),
            Status = "posted",
            PaymentMethod = request.PaymentMethod.Trim().ToLowerInvariant(),
            Description = $"Payroll payment for line {request.PayrollLineId:N}",
            SourceType = "payroll_payment",
            SourceReference = request.Reference,
            RecordedByUserId = actor.Value
        };
        _db.CashExpenses.Add(expense);

        var payment = new PayrollPayment
        {
            TenantId = tenantId,
            PayrollPeriodId = payrollPeriodId,
            PayrollLineId = request.PayrollLineId,
            Amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero),
            PaidDate = request.PaidDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            PaymentMethod = request.PaymentMethod.Trim().ToLowerInvariant(),
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            Status = "posted",
            PaidByAppUserId = actor.Value,
            CashExpense = expense
        };
        _db.PayrollPayments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return Result<PayrollPaymentDto>.Success(ToDto(payment));
    }

    public async Task<Result<List<PayrollPaymentDto>>> ListAsync(
        Guid tenantId,
        Guid payrollPeriodId,
        CancellationToken ct = default)
    {
        var payments = await _db.PayrollPayments.AsNoTracking()
            .Where(payment => payment.TenantId == tenantId && payment.PayrollPeriodId == payrollPeriodId)
            .OrderByDescending(payment => payment.PaidDate)
            .Select(payment => new PayrollPaymentDto
            {
                Id = payment.Id,
                PayrollPeriodId = payment.PayrollPeriodId,
                PayrollLineId = payment.PayrollLineId,
                Amount = payment.Amount,
                PaidDate = payment.PaidDate,
                PaymentMethod = payment.PaymentMethod,
                Reference = payment.Reference,
                Status = payment.Status
            })
            .ToListAsync(ct);
        return Result<List<PayrollPaymentDto>>.Success(payments);
    }

    private static PayrollPaymentDto ToDto(PayrollPayment payment) => new()
    {
        Id = payment.Id,
        PayrollPeriodId = payment.PayrollPeriodId,
        PayrollLineId = payment.PayrollLineId,
        Amount = payment.Amount,
        PaidDate = payment.PaidDate,
        PaymentMethod = payment.PaymentMethod,
        Reference = payment.Reference,
        Status = payment.Status
    };
}
