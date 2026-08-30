namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Expenses;

public interface ICashExpenseService
{
    Task<Result<CashExpenseDto>> CreateAsync(
        Guid tenantId, Guid userId, CreateCashExpenseRequest request, CancellationToken ct = default);

    Task<Result<List<CashExpenseDto>>> ListAsync(
        Guid tenantId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    Task<Result<CashExpenseDto>> UpdateAsync(
        Guid tenantId, Guid id, UpdateCashExpenseRequest request, CancellationToken ct = default);
}
