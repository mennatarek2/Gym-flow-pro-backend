namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Expenses;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public sealed class CashExpenseService : ICashExpenseService
{
    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "posted", "void" };

    private readonly GymFlowProDbContext _db;

    public CashExpenseService(GymFlowProDbContext db)
    {
        _db = db;
    }

    public async Task<Result<CashExpenseDto>> CreateAsync(
        Guid tenantId,
        Guid userId,
        CreateCashExpenseRequest request,
        CancellationToken ct = default)
    {
        if (request.Amount <= 0)
            return Result<CashExpenseDto>.Failure("Expense amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(request.Category))
            return Result<CashExpenseDto>.Failure("Expense category is required.");

        var expense = new CashExpense
        {
            TenantId = tenantId,
            ExpenseDate = request.ExpenseDate,
            Category = request.Category.Trim(),
            Amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            RecordedByUserId = userId,
            Status = "posted"
        };
        _db.CashExpenses.Add(expense);
        await _db.SaveChangesAsync(ct);
        return Result<CashExpenseDto>.Success(ToDto(expense));
    }

    public async Task<Result<List<CashExpenseDto>>> ListAsync(
        Guid tenantId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        if (from.HasValue != to.HasValue || (from.HasValue && from > to))
            return Result<List<CashExpenseDto>>.Failure("Invalid expense date range.");

        var query = _db.CashExpenses.AsNoTracking()
            .Where(expense => expense.TenantId == tenantId);
        if (from.HasValue)
            query = query.Where(expense => expense.ExpenseDate >= from && expense.ExpenseDate <= to);

        var expenses = await query
            .OrderByDescending(expense => expense.ExpenseDate)
            .ThenByDescending(expense => expense.CreatedAtUtc)
            .ToListAsync(ct);
        return Result<List<CashExpenseDto>>.Success(expenses.Select(ToDto).ToList());
    }

    public async Task<Result<CashExpenseDto>> UpdateAsync(
        Guid tenantId,
        Guid id,
        UpdateCashExpenseRequest request,
        CancellationToken ct = default)
    {
        var expense = await _db.CashExpenses
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == id, ct);
        if (expense == null)
            return Result<CashExpenseDto>.Failure("Expense not found.");

        if (request.Amount.HasValue && request.Amount <= 0)
            return Result<CashExpenseDto>.Failure("Expense amount must be greater than zero.");
        if (request.Category != null && string.IsNullOrWhiteSpace(request.Category))
            return Result<CashExpenseDto>.Failure("Expense category is required.");
        if (request.Status != null && !ValidStatuses.Contains(request.Status))
            return Result<CashExpenseDto>.Failure("Expense status must be posted or void.");

        if (request.ExpenseDate.HasValue)
            expense.ExpenseDate = request.ExpenseDate.Value;
        if (request.Category != null)
            expense.Category = request.Category.Trim();
        if (request.Amount.HasValue)
            expense.Amount = decimal.Round(request.Amount.Value, 2, MidpointRounding.AwayFromZero);
        if (request.Status != null)
            expense.Status = request.Status.Trim().ToLowerInvariant();
        if (request.Note != null)
            expense.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        expense.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result<CashExpenseDto>.Success(ToDto(expense));
    }

    private static CashExpenseDto ToDto(CashExpense expense) => new()
    {
        Id = expense.Id,
        ExpenseDate = expense.ExpenseDate,
        Category = expense.Category,
        Amount = expense.Amount,
        Status = expense.Status,
        Note = expense.Note,
        RecordedByUserId = expense.RecordedByUserId,
        CreatedAtUtc = expense.CreatedAtUtc
    };
}
