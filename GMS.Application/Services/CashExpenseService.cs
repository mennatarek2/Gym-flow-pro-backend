namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Expenses;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public sealed class CashExpenseService : ICashExpenseService
{
    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "posted", "void" };
    private static readonly HashSet<string> ValidPaymentMethods =
        new(StringComparer.OrdinalIgnoreCase)
        { "cash", "card", "bank_transfer", "wallet", "other" };

    private readonly GymFlowProDbContext _db;
    private readonly IAuditService? _audit;

    public CashExpenseService(GymFlowProDbContext db, IAuditService? audit = null)
    {
        _db = db;
        _audit = audit;
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
        if (!CashExpenseCatalog.IsKnownCategory(request.Category))
            return Result<CashExpenseDto>.Failure(
                "Expense category must be a structured running-cost category.");
        if (string.IsNullOrWhiteSpace(request.Description))
            return Result<CashExpenseDto>.Failure("Expense type is required.");
        if (!CashExpenseCatalog.IsKnownType(request.Category, request.Description))
            return Result<CashExpenseDto>.Failure("Expense type is invalid for the selected category.");
        var paymentMethod = request.PaymentMethod?.Trim().ToLowerInvariant();
        if (paymentMethod == null || !ValidPaymentMethods.Contains(paymentMethod))
            return Result<CashExpenseDto>.Failure("Expense payment method is invalid.");
        if (string.Equals(paymentMethod, "cash", StringComparison.OrdinalIgnoreCase) && !request.ShiftId.HasValue)
            return Result<CashExpenseDto>.Failure("Cash running costs require an open shift.");
        if (request.ShiftId.HasValue)
        {
            var shift = await _db.Shifts.FirstOrDefaultAsync(item =>
                item.Id == request.ShiftId.Value
                && item.TenantId == tenantId
                && item.Status == "open", ct);
            if (shift == null)
                return Result<CashExpenseDto>.Failure("The selected cash shift is not open for this expense.");
        }

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var replay = await _db.CashExpenses
                .AsNoTracking()
                .FirstOrDefaultAsync(expense => expense.TenantId == tenantId
                    && expense.IdempotencyKey == request.IdempotencyKey.Trim(), ct);
            if (replay != null)
                return Result<CashExpenseDto>.Success(ToDto(replay));
        }

        var actorAppUserId = await StaffAppUserProvisioner.ResolveOrCreateAsync(_db, tenantId, userId, ct);
        if (!actorAppUserId.HasValue && !_db.Database.IsRelational())
            actorAppUserId = userId;
        if (!actorAppUserId.HasValue)
            return Result<CashExpenseDto>.Failure("Authenticated staff profile was not found for this gym.");

        var expense = new CashExpense
        {
            TenantId = tenantId,
            ExpenseDate = request.ExpenseDate,
            Category = request.Category.Trim(),
            Amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            PaymentMethod = paymentMethod,
            Payee = Clean(request.Payee),
            Description = Clean(request.Description),
            SourceType = Clean(request.SourceType) ?? CashExpenseCatalog.ManualRunningCostSourceType,
            SourceReference = Clean(request.SourceReference),
            IdempotencyKey = Clean(request.IdempotencyKey),
            ShiftId = request.ShiftId,
            RecordedByUserId = actorAppUserId.Value,
            Status = "posted"
        };
        _db.CashExpenses.Add(expense);
        var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            await _db.SaveChangesAsync(ct);
            if (request.ShiftId.HasValue && paymentMethod == "cash")
            {
                _db.CashMovements.Add(new CashMovement
                {
                    TenantId = tenantId,
                    ShiftId = request.ShiftId.Value,
                    Type = "paid_out",
                    Amount = -expense.Amount,
                    ReferenceId = expense.Id,
                    Reason = expense.Description ?? expense.Note ?? expense.Category,
                    CreatedByUserId = actorAppUserId!.Value
                });
            }
            await _db.SaveChangesAsync(ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            if (transaction != null)
                await transaction.RollbackAsync(ct);
            return Result<CashExpenseDto>.Failure(MapSaveError(ex));
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
        if (_audit != null)
            await _audit.LogAsync("cash_expense.posted", "CashExpense", expense.Id, null,
                new { expense.Amount, expense.Category, expense.PaymentMethod, expense.ShiftId });
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
        if (request.ShiftId.HasValue)
        {
            var shift = await _db.Shifts.FirstOrDefaultAsync(item =>
                item.Id == request.ShiftId.Value
                && item.TenantId == tenantId
                && item.Status == "open", ct);
            if (shift == null)
                return Result<CashExpenseDto>.Failure("The selected cash shift is not open for this expense.");
        }

        var oldCashAmount = expense.Status == "posted"
            && string.Equals(expense.PaymentMethod, "cash", StringComparison.OrdinalIgnoreCase)
            && expense.ShiftId.HasValue
            ? expense.Amount
            : 0m;
        var newStatus = request.Status?.Trim().ToLowerInvariant() ?? expense.Status;
        var newPaymentMethod = request.PaymentMethod?.Trim().ToLowerInvariant() ?? expense.PaymentMethod;
        var newShiftId = request.ShiftId ?? expense.ShiftId;
        var newAmount = request.Amount.HasValue
            ? decimal.Round(request.Amount.Value, 2, MidpointRounding.AwayFromZero)
            : expense.Amount;
        var newCashAmount = newStatus == "posted"
            && string.Equals(newPaymentMethod, "cash", StringComparison.OrdinalIgnoreCase)
            && newShiftId.HasValue
            ? newAmount
            : 0m;

        if (oldCashAmount != newCashAmount)
        {
            var originalMovement = await _db.CashMovements
                .FirstOrDefaultAsync(movement => movement.TenantId == tenantId
                    && movement.ReferenceId == expense.Id
                    && movement.Type == "paid_out", ct);
            if (originalMovement != null && expense.ShiftId.HasValue && expense.ShiftId != newShiftId)
            {
                var originalShiftOpen = await _db.Shifts.AnyAsync(shift =>
                    shift.Id == expense.ShiftId.Value
                    && shift.TenantId == tenantId
                    && shift.Status == "open", ct);
                if (!originalShiftOpen)
                    return Result<CashExpenseDto>.Failure("A posted cash expense on a closed shift cannot be moved or edited.");
            }
            if (newCashAmount > 0m && !newShiftId.HasValue)
                return Result<CashExpenseDto>.Failure("A cash expense requires an open shift.");
        }

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
        if (request.PaymentMethod != null)
        {
            if (!ValidPaymentMethods.Contains(request.PaymentMethod))
                return Result<CashExpenseDto>.Failure("Expense payment method is invalid.");
            expense.PaymentMethod = request.PaymentMethod.Trim().ToLowerInvariant();
        }
        if (request.Payee != null)
            expense.Payee = Clean(request.Payee);
        if (request.Description != null)
            expense.Description = Clean(request.Description);
        if (request.SourceType != null)
            expense.SourceType = Clean(request.SourceType);
        if (request.SourceReference != null)
            expense.SourceReference = Clean(request.SourceReference);
        if (request.ShiftId.HasValue)
            expense.ShiftId = request.ShiftId;
        expense.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        if (oldCashAmount != newCashAmount)
        {
            _db.CashMovements.Add(new CashMovement
            {
                TenantId = tenantId,
                ShiftId = expense.ShiftId ?? request.ShiftId!.Value,
                Type = "paid_out",
                Amount = oldCashAmount - newCashAmount,
                ReferenceId = expense.Id,
                Reason = $"Expense {expense.Id:N} adjustment",
                CreatedByUserId = expense.RecordedByUserId
            });
            await _db.SaveChangesAsync(ct);
        }
        if (_audit != null)
            await _audit.LogAsync("cash_expense.updated", "CashExpense", expense.Id, null,
                new { expense.Status, expense.Amount, expense.PaymentMethod, expense.ShiftId });
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
        PaymentMethod = expense.PaymentMethod,
        Payee = expense.Payee,
        Description = expense.Description,
        SourceType = expense.SourceType,
        SourceReference = expense.SourceReference,
        IdempotencyKey = expense.IdempotencyKey,
        ShiftId = expense.ShiftId,
        RecordedByUserId = expense.RecordedByUserId,
        CreatedAtUtc = expense.CreatedAtUtc
    };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string MapSaveError(DbUpdateException ex)
    {
        var detail = ex.InnerException?.Message ?? ex.Message;
        if (detail.Contains("SourceType", StringComparison.OrdinalIgnoreCase))
            return "Could not save running cost because expense source metadata is missing.";
        return "Could not save running cost. Check the form and try again.";
    }
}
