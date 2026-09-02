namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Sales;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public sealed class SaleAdjustmentService : ISaleAdjustmentService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;

    public SaleAdjustmentService(GymFlowProDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<List<SaleAdjustmentDto>>> ListAsync(
        Guid tenantId,
        Guid? saleId = null,
        CancellationToken ct = default)
    {
        var query = _db.SaleAdjustments.AsNoTracking()
            .Where(item => item.TenantId == tenantId);
        if (saleId.HasValue)
            query = query.Where(item => item.SaleId == saleId.Value);

        var items = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(ct);
        return Result<List<SaleAdjustmentDto>>.Success(items.Select(ToDto).ToList());
    }

    public async Task<Result<SaleAdjustmentDto>> CreateAsync(
        Guid tenantId,
        Guid identityUserId,
        CreateSaleAdjustmentRequest request,
        CancellationToken ct = default)
    {
        var type = request.Type.Trim().ToLowerInvariant();
        if (type is not ("write_off" or "cancellation"))
            return Result<SaleAdjustmentDto>.Failure("Adjustment type is invalid.");
        if (request.Amount <= 0m)
            return Result<SaleAdjustmentDto>.Failure("Adjustment amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<SaleAdjustmentDto>.Failure("An adjustment reason is required.");

        var actor = await _db.AppUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(user => user.TenantId == tenantId
                && user.IsActive
                && user.UserId == identityUserId.ToString(), ct);
        if (actor == null)
            return Result<SaleAdjustmentDto>.Failure("Authenticated staff profile was not found.");

        var sale = await _db.Sales.FirstOrDefaultAsync(item =>
            item.Id == request.SaleId
            && item.TenantId == tenantId
            && item.Status != "refunded"
            && item.Status != "cancelled"
            && item.AmountDue > 0m, ct);
        if (sale == null)
            return Result<SaleAdjustmentDto>.Failure("Sale has no adjustable outstanding balance.");

        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount > sale.AmountDue)
            return Result<SaleAdjustmentDto>.Failure("Adjustment exceeds the outstanding sale balance.");

        sale.AmountDue = decimal.Round(sale.AmountDue - amount, 2, MidpointRounding.AwayFromZero);
        sale.Status = sale.AmountDue == 0m
            ? (type == "cancellation" ? "cancelled" : "written_off")
            : "partially_paid";
        sale.UpdatedAtUtc = DateTime.UtcNow;

        var adjustment = new SaleAdjustment
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            Amount = amount,
            Type = type,
            Status = "posted",
            Reason = request.Reason.Trim(),
            CreatedByUserId = actor.Id
        };
        _db.SaleAdjustments.Add(adjustment);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("sale.adjustment.posted", "SaleAdjustment", adjustment.Id, null,
            new { adjustment.SaleId, adjustment.Amount, adjustment.Type });
        return Result<SaleAdjustmentDto>.Success(ToDto(adjustment));
    }

    public async Task<Result<SaleBalanceReconciliationDto>> ReconcileBalanceAsync(
        Guid tenantId,
        Guid identityUserId,
        Guid saleId,
        CancellationToken ct = default)
    {
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, ct)
            : null;

        var actor = await _db.AppUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(user => user.TenantId == tenantId
                && user.IsActive
                && user.UserId == identityUserId.ToString(), ct);
        if (actor == null)
            return Result<SaleBalanceReconciliationDto>.Failure(
                "Authenticated staff profile was not found.");

        var sale = await _db.Sales.FirstOrDefaultAsync(item =>
            item.Id == saleId && item.TenantId == tenantId, ct);
        if (sale == null)
            return Result<SaleBalanceReconciliationDto>.Failure("Sale not found.");

        var allocated = await _db.PaymentTransactions
            .Where(payment => payment.TenantId == tenantId
                && payment.SaleId == saleId
                && payment.Status == "success"
                && payment.Amount > 0m)
            .SumAsync(payment => (decimal?)payment.Amount, ct) ?? 0m;
        var adjustments = await _db.SaleAdjustments
            .Where(adjustment => adjustment.TenantId == tenantId
                && adjustment.SaleId == saleId
                && adjustment.Status == "posted")
            .SumAsync(adjustment => (decimal?)adjustment.Amount, ct) ?? 0m;
        var canonicalDue = Math.Max(0m, decimal.Round(
            sale.Total - allocated - adjustments, 2, MidpointRounding.AwayFromZero));
        var previousDue = sale.AmountDue;
        var changed = Math.Abs(previousDue - canonicalDue) > 0.01m;

        if (changed)
        {
            sale.AmountDue = canonicalDue;
            sale.Status = canonicalDue == 0m
                ? (adjustments > 0m ? "written_off" : "completed")
                : "partially_paid";
            sale.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        if (transaction != null)
            await transaction.CommitAsync(ct);

        var result = new SaleBalanceReconciliationDto
        {
            SaleId = sale.Id,
            PreviousAmountDue = previousDue,
            CanonicalAmountDue = canonicalDue,
            AllocatedPayments = allocated,
            PostedAdjustments = adjustments,
            Status = changed ? "reconciled" : "already_reconciled"
        };
        if (changed)
            await _audit.LogAsync(
                "sale.balance.reconciled",
                "Sale",
                sale.Id,
                new { AmountDue = previousDue },
                new
                {
                    AmountDue = canonicalDue,
                    AllocatedPayments = allocated,
                    PostedAdjustments = adjustments,
                    actor.Id
                },
                tenantId);
        return Result<SaleBalanceReconciliationDto>.Success(result);
    }

    private static SaleAdjustmentDto ToDto(SaleAdjustment item) => new()
    {
        Id = item.Id,
        SaleId = item.SaleId,
        Amount = item.Amount,
        Type = item.Type,
        Status = item.Status,
        Reason = item.Reason,
        CreatedByUserId = item.CreatedByUserId,
        CreatedAtUtc = item.CreatedAtUtc
    };
}
