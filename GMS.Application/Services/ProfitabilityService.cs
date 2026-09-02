namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Reports;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Computes the canonical financial view without turning payment rows into
/// revenue or supplier purchases into operating expenses.
/// </summary>
public sealed class ProfitabilityService : IProfitabilityService
{
    private static readonly TimeZoneInfo CairoTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly GymFlowProDbContext _db;
    private readonly IAuditService? _audit;

    public ProfitabilityService(GymFlowProDbContext db, IAuditService? audit = null)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<ProfitabilityDto>> GetAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (from > to)
            return Result<ProfitabilityDto>.Failure("From date must be before To date.");

        var range = MembershipOperational.CairoInclusiveRangeUtc(from, to);

        var payments = await _db.PaymentTransactions.AsNoTracking()
            .Where(p => p.TenantId == tenantId
                && p.Status == "success"
                && p.Amount > 0
                && p.PaidAtUtc >= range.UtcStart
                && p.PaidAtUtc < range.UtcEndExclusive)
            .Select(p => new PaymentFact(p.Amount, p.Method, p.SettlementStatus))
            .ToListAsync(ct);

        var refunds = await _db.Refunds.AsNoTracking()
            .Where(r => r.TenantId == tenantId
                && r.Status == "executed"
                && r.ExecutedAt >= range.UtcStart
                && r.ExecutedAt < range.UtcEndExclusive)
            .Select(r => new { r.Amount, r.Method, r.ExecutedAt })
            .ToListAsync(ct);

        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.TenantId == tenantId
                && s.CreatedAtUtc >= range.UtcStart
                && s.CreatedAtUtc < range.UtcEndExclusive)
            .Select(s => new { s.Total, s.AmountDue, s.Id, s.CreatedAtUtc })
            .ToListAsync(ct);
        var saleIds = sales.Select(sale => sale.Id).ToList();
        var allocationTotals = await _db.PaymentTransactions.AsNoTracking()
            .Where(payment => payment.TenantId == tenantId
                && payment.SaleId.HasValue
                && saleIds.Contains(payment.SaleId.Value)
                && payment.Status == "success"
                && payment.Amount > 0m)
            .GroupBy(payment => payment.SaleId!.Value)
            .Select(group => new { SaleId = group.Key, Amount = group.Sum(payment => payment.Amount) })
            .ToDictionaryAsync(item => item.SaleId, item => item.Amount, ct);
        var adjustmentTotals = await _db.SaleAdjustments.AsNoTracking()
            .Where(adjustment => adjustment.TenantId == tenantId
                && saleIds.Contains(adjustment.SaleId)
                && adjustment.Status == "posted")
            .GroupBy(adjustment => adjustment.SaleId)
            .Select(group => new { SaleId = group.Key, Amount = group.Sum(adjustment => adjustment.Amount) })
            .ToDictionaryAsync(item => item.SaleId, item => item.Amount, ct);
        var allocationMismatchCount = sales.Count(sale =>
        {
            allocationTotals.TryGetValue(sale.Id, out var allocated);
            adjustmentTotals.TryGetValue(sale.Id, out var adjustments);
            var expected = Math.Max(0m, decimal.Round(
                sale.Total - allocated - adjustments, 2, MidpointRounding.AwayFromZero));
            return Math.Abs(sale.AmountDue - expected) > 0.01m;
        });

        var cancellationAdjustments = await _db.SaleAdjustments.AsNoTracking()
            .Where(adjustment => adjustment.TenantId == tenantId
                && adjustment.Status == "posted"
                && adjustment.Type == "cancellation"
                && adjustment.CreatedAtUtc >= range.UtcStart
                && adjustment.CreatedAtUtc < range.UtcEndExclusive)
            .Select(adjustment => new { adjustment.Amount, adjustment.CreatedAtUtc })
            .ToListAsync(ct);
        var revenueAdjustments = cancellationAdjustments.Sum(adjustment => adjustment.Amount);

        var retailLines = await _db.SaleLines.AsNoTracking()
            .Where(line => line.TenantId == tenantId
                && line.LineType == "retail"
                && line.Sale != null
                && line.Sale.CreatedAtUtc >= range.UtcStart
                && line.Sale.CreatedAtUtc < range.UtcEndExclusive)
            .Select(line => new { line.SaleId, line.CogsAmount })
            .ToListAsync(ct);
        var partiallyRefundedRetailSaleIds = await _db.Refunds.AsNoTracking()
            .Where(refund => refund.TenantId == tenantId
                && refund.Status == "executed"
                && refund.ExecutedAt >= range.UtcStart
                && refund.ExecutedAt < range.UtcEndExclusive)
            .Where(refund => refund.Sale != null
                && refund.Sale.Status == "partially_refunded"
                && refund.Sale.Lines.Any(line => line.TenantId == tenantId && line.LineType == "retail"))
            .Select(refund => refund.SaleId)
            .Distinct()
            .ToListAsync(ct);
        var fullyRefundedRetailSaleIds = await _db.Sales.AsNoTracking()
            .Where(sale => sale.TenantId == tenantId
                && sale.Status == "refunded"
                && sale.CreatedAtUtc >= range.UtcStart
                && sale.CreatedAtUtc < range.UtcEndExclusive
                && sale.Lines.Any(line => line.TenantId == tenantId && line.LineType == "retail"))
            .Select(sale => sale.Id)
            .ToListAsync(ct);

        var expenses = await _db.CashExpenses.AsNoTracking()
            .Where(expense => expense.TenantId == tenantId
                && expense.Status == "posted"
                && expense.Category != "payroll"
                && expense.SourceType != "payroll_payment"
                && expense.ExpenseDate >= from
                && expense.ExpenseDate <= to)
            .SumAsync(expense => (decimal?)expense.Amount, ct) ?? 0m;

        var payrollPeriods = await _db.PayrollPeriods.AsNoTracking()
            .Where(period => period.TenantId == tenantId
                && (period.Status == PayrollPeriodStatuses.Approved
                    || period.Status == PayrollPeriodStatuses.Closed))
            .Include(period => period.Lines)
            .ToListAsync(ct);

        var payrollPeriodsInRange = payrollPeriods
            .Where(period => IsMonthInRange(period.Year, period.Month, from, to))
            .ToList();
        var payrollLinesInRange = payrollPeriodsInRange
            .SelectMany(period => period.Lines)
            .ToList();
        var payrollCoverageStatus = payrollPeriodsInRange.Count == 0
            ? "NO_PAYROLL_PERIOD"
            : payrollLinesInRange.Count == 0
                || payrollLinesInRange.Any(line => line.NetSalary < 0m)
                ? "PAYROLL_DATA_INCOMPLETE"
                : "COMPLETE";
        var payrollInRange = payrollLinesInRange.Sum(line => line.NetSalary);
        var payrollAvailable = payrollCoverageStatus == "COMPLETE";
        var payrollCashPayments = await _db.PayrollPayments.AsNoTracking()
            .Where(payment => payment.TenantId == tenantId
                && payment.Status == "posted"
                && payment.PaidDate >= from
                && payment.PaidDate <= to)
            .SumAsync(payment => (decimal?)payment.Amount, ct) ?? 0m;

        var supplierPaymentEntries = await _db.SupplierLedgerEntries.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId
                && entry.Amount < 0m
                && entry.Reason == SupplierLedgerReasons.Payment
                && (entry.EffectiveAtUtc ?? entry.CreatedAtUtc) >= range.UtcStart
                && (entry.EffectiveAtUtc ?? entry.CreatedAtUtc) < range.UtcEndExclusive)
            .Select(entry => new { entry.Amount, entry.ReferenceId, entry.ReferenceType })
            .ToListAsync(ct);
        var supplierCashPaymentsAvailable = supplierPaymentEntries.All(entry =>
            entry.ReferenceId.HasValue
            && string.Equals(entry.ReferenceType, "CashMovement", StringComparison.OrdinalIgnoreCase));
        var supplierPayments = supplierCashPaymentsAvailable
            ? supplierPaymentEntries.Sum(entry => -entry.Amount)
            : 0m;

        var arQuery = _db.Sales.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.AmountDue > 0m
                && s.Status != "refunded"
                && s.Status != "cancelled"
                && s.CreatedAtUtc < range.UtcEndExclusive);
        var ar = await arQuery
            .SumAsync(s => (decimal?)s.AmountDue, ct) ?? 0m;
        var arCount = await arQuery.CountAsync(ct);

        var ap = await _db.SupplierLedgerEntries.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId
                && (entry.EffectiveAtUtc ?? entry.CreatedAtUtc) < range.UtcEndExclusive)
            .SumAsync(entry => (decimal?)entry.Amount, ct) ?? 0m;

        var cogsCoverageComplete = retailLines.All(line => line.CogsAmount.HasValue)
            && partiallyRefundedRetailSaleIds.Count == 0;
        var cogs = cogsCoverageComplete
            ? retailLines.Sum(line => fullyRefundedRetailSaleIds.Contains(line.SaleId)
                ? -line.CogsAmount!.Value
                : line.CogsAmount!.Value)
            : (decimal?)null;
        var breakdownLines = await _db.SaleLines.AsNoTracking()
            .Where(line => line.TenantId == tenantId && saleIds.Contains(line.SaleId))
            .Select(line => new { line.SaleId, line.LineType })
            .ToListAsync(ct);
        var breakdownTotals = new Dictionary<string, (decimal Amount, int Count)>(
            StringComparer.Ordinal)
        {
            ["memberships"] = (0m, 0),
            ["renewals"] = (0m, 0),
            ["products"] = (0m, 0),
            ["classes"] = (0m, 0)
        };
        foreach (var sale in sales)
        {
            var types = breakdownLines
                .Where(line => line.SaleId == sale.Id)
                .Select(line => line.LineType.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
            var key = types.Contains("retail")
                ? "products"
                : types.Contains("membership")
                    ? "memberships"
                    : "classes";
            var current = breakdownTotals[key];
            breakdownTotals[key] = (current.Amount + sale.Total, current.Count + 1);
        }
        var revenueBreakdown = breakdownTotals
            .Select(item => new ProfitabilityBreakdownDto
            {
                Key = item.Key,
                Amount = item.Value.Amount,
                Count = item.Value.Count
            })
            .ToList();
        var refundsTotal = refunds.Sum(refund => refund.Amount);
        var cashRefunds = refunds
            .Where(refund => !string.Equals(refund.Method, "credit", StringComparison.OrdinalIgnoreCase))
            .Sum(refund => refund.Amount);
        var creditRefunds = refunds
            .Where(refund => string.Equals(refund.Method, "credit", StringComparison.OrdinalIgnoreCase))
            .Sum(refund => refund.Amount);
        var collections = payments.Sum(payment => payment.Amount);
        var settledCash = payments
            .Where(IsSettledCash)
            .Sum(payment => payment.Amount);
        var grossRevenue = sales.Sum(sale => sale.Total);
        var revenue = grossRevenue - refundsTotal - revenueAdjustments;
        var grossProfit = cogs.HasValue
            ? revenue - cogs.Value
            : (decimal?)null;
        var netProfit = grossProfit.HasValue && payrollAvailable
            ? grossProfit.Value - expenses - payrollInRange
            : (decimal?)null;
        var cashOutflows = cashRefunds + expenses + payrollCashPayments + supplierPayments;
        var netCashFlow = settledCash - cashOutflows;
        var revenueTrend = new List<ProfitabilityTrendPointDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var dayRange = MembershipOperational.CairoInclusiveRangeUtc(day, day);
            var daySales = sales
                .Where(sale => sale.CreatedAtUtc >= dayRange.UtcStart
                    && sale.CreatedAtUtc < dayRange.UtcEndExclusive)
                .Sum(sale => sale.Total);
            var dayRefunds = refunds
                .Where(refund => refund.ExecutedAt.HasValue
                    && refund.ExecutedAt.Value >= dayRange.UtcStart
                    && refund.ExecutedAt.Value < dayRange.UtcEndExclusive)
                .Sum(refund => refund.Amount);
            var dayAdjustments = cancellationAdjustments
                .Where(adjustment => adjustment.CreatedAtUtc >= dayRange.UtcStart
                    && adjustment.CreatedAtUtc < dayRange.UtcEndExclusive)
                .Sum(adjustment => adjustment.Amount);
            revenueTrend.Add(new ProfitabilityTrendPointDto
            {
                Date = day,
                Value = daySales - dayRefunds - dayAdjustments
            });
        }
        var issues = new List<string>();

        if (payments.Any(payment => !IsSettledCash(payment)))
            issues.Add("settlement_data_incomplete");
        if (allocationMismatchCount > 0)
            issues.Add("payment_allocation_mismatch");
        if (!supplierCashPaymentsAvailable && supplierPaymentEntries.Count > 0)
            issues.Add("supplier_cash_evidence_unavailable");
        if (!cogsCoverageComplete)
            issues.Add("cogs_unavailable");
        if (partiallyRefundedRetailSaleIds.Count > 0)
            issues.Add("retail_refund_cogs_unavailable");
        if (payrollCoverageStatus == "NO_PAYROLL_PERIOD")
            issues.Add("no_payroll_period");
        else if (!payrollAvailable)
            issues.Add("payroll_data_incomplete");
        if (retailLines.Count == 0)
            issues.Add("no_retail_lines");

        var trustStates = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Collections"] = "CONDITIONALLY_TRUSTWORTHY",
            ["SettledCash"] = payments.All(IsSettledCash)
                ? "TRUSTWORTHY"
                : "UNAVAILABLE",
            ["Revenue"] = "CONDITIONALLY_TRUSTWORTHY",
            ["Refunds"] = "CONDITIONALLY_TRUSTWORTHY",
            ["Cogs"] = cogsCoverageComplete
                ? "TRUSTWORTHY"
                : "UNAVAILABLE",
            ["GrossProfit"] = grossProfit.HasValue
                ? "TRUSTWORTHY"
                : "UNAVAILABLE",
            ["OperatingExpenses"] = "CONDITIONALLY_TRUSTWORTHY",
            ["Payroll"] = payrollAvailable
                ? "TRUSTWORTHY"
                : "UNAVAILABLE",
            ["NetProfit"] = netProfit.HasValue
                ? "CONDITIONALLY_TRUSTWORTHY"
                : "UNAVAILABLE",
            ["AccountsReceivable"] = "CONDITIONALLY_TRUSTWORTHY",
            ["AccountsPayable"] = "CONDITIONALLY_TRUSTWORTHY",
            ["CashFlow"] = !issues.Contains("settlement_data_incomplete")
                && supplierCashPaymentsAvailable
                    ? "TRUSTWORTHY"
                    : "UNAVAILABLE"
        };

        return Result<ProfitabilityDto>.Success(new ProfitabilityDto
        {
            CalculationVersion = "financial-v1",
            From = from,
            To = to,
            Collections = collections,
            SettledCashInflow = settledCash,
            SettledCashAvailable = payments.All(IsSettledCash),
            Revenue = revenue,
            RevenueAdjustments = revenueAdjustments,
            Refunds = refundsTotal,
            CashRefunds = cashRefunds,
            CreditRefunds = creditRefunds,
            Cogs = cogs,
            OperatingExpenses = expenses,
            PayrollExpense = payrollAvailable ? payrollInRange : null,
            PayrollCoverageStatus = payrollCoverageStatus,
            PayrollCashDisbursements = payrollCashPayments,
            SupplierCashPayments = supplierPayments,
            GrossProfit = grossProfit,
            NetProfit = netProfit,
            NetProfitAvailable = netProfit.HasValue,
            ProfitMargin = netProfit.HasValue && revenue > 0m
                ? decimal.Round(netProfit.Value / revenue * 100m, 2)
                : null,
            CashOutflows = cashOutflows,
            NetCashFlow = netCashFlow,
            CashFlowAvailable = !issues.Contains("settlement_data_incomplete")
                && supplierCashPaymentsAvailable,
            SupplierCashPaymentsAvailable = supplierCashPaymentsAvailable,
            AccountsReceivable = ar,
            AccountsReceivableCount = arCount,
            AccountsPayable = ap,
            CogsAvailable = cogsCoverageComplete,
            PayrollAvailable = payrollAvailable,
            AccountsPayableAvailable = true,
            DataIssues = issues,
            TrustStates = trustStates,
            RevenueBreakdown = revenueBreakdown,
            RevenueTrend = revenueTrend
        });
    }

    public async Task<Result<CogsBackfillDto>> BackfillCogsAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var lines = await _db.SaleLines
            .Where(line => line.TenantId == tenantId
                && line.LineType == "retail")
            .ToListAsync(ct);

        var lineIds = lines.Select(line => line.Id).ToList();
        var movements = await _db.StockMovements.AsNoTracking()
            .Where(movement => movement.TenantId == tenantId
                && movement.ReferenceType == StockReferenceTypes.SaleLine
                && movement.Reason == StockMovementReasons.Sale
                && movement.ReferenceId.HasValue
                && lineIds.Contains(movement.ReferenceId.Value)
                && movement.UnitCost.HasValue)
            .Select(movement => new
            {
                ReferenceId = movement.ReferenceId!.Value,
                ProductId = movement.ProductId,
                Qty = Math.Abs(movement.QtyDelta),
                UnitCost = movement.UnitCost!.Value
            })
            .ToListAsync(ct);

        var skipped = new List<Guid>();
        var items = new List<CogsBackfillItemDto>();
        var backfilled = 0;
        foreach (var line in lines)
        {
            if (line.CogsAmount.HasValue && line.UnitCost.HasValue)
            {
                items.Add(new CogsBackfillItemDto
                {
                    SaleLineId = line.Id,
                    OldCost = line.CogsAmount,
                    ReconstructedCost = line.CogsAmount,
                    Evidence = "Existing immutable SaleLine snapshot",
                    Status = "ALREADY_RELIABLE"
                });
                continue;
            }

            var evidence = movements.Where(movement => movement.ReferenceId == line.Id).ToList();
            var quantity = evidence.Sum(item => item.Qty);
            var productMatches = evidence.All(item =>
                line.ReferenceId.HasValue && item.ProductId == line.ReferenceId.Value);
            if (evidence.Count == 0 || !productMatches || Math.Abs(quantity - line.Qty) > 0.005m)
            {
                skipped.Add(line.Id);
                items.Add(new CogsBackfillItemDto
                {
                    SaleLineId = line.Id,
                    OldCost = line.CogsAmount,
                    Evidence = evidence.Count == 0 ? "No matching costed sale movement" : "Ambiguous or mismatched sale movement evidence",
                    Status = "UNAVAILABLE"
                });
                continue;
            }

            var cost = evidence.Sum(item => item.Qty * item.UnitCost);
            line.CogsAmount = decimal.Round(cost, 2, MidpointRounding.AwayFromZero);
            line.UnitCost = line.Qty == 0m
                ? 0m
                : decimal.Round(cost / line.Qty, 2, MidpointRounding.AwayFromZero);
            line.UpdatedAtUtc = DateTime.UtcNow;
            backfilled++;
            items.Add(new CogsBackfillItemDto
            {
                SaleLineId = line.Id,
                OldCost = null,
                Evidence = $"StockMovement sale allocations: {evidence.Count}",
                ReconstructedCost = line.CogsAmount,
                Status = "RECONSTRUCTABLE"
            });
        }

        if (backfilled > 0)
            await _db.SaveChangesAsync(ct);

        var result = new CogsBackfillDto
        {
            Scanned = lines.Count,
            Backfilled = backfilled,
            Skipped = skipped.Count,
            SkippedSaleLineIds = skipped,
            Items = items
        };
        if (_audit != null)
            await _audit.LogAsync(
                "financial.cogs_backfill",
                "SaleLine",
                null,
                null,
                new { result.Scanned, result.Backfilled, result.Skipped, result.SkippedSaleLineIds },
                tenantId);
        return Result<CogsBackfillDto>.Success(result);
    }

    private static bool IsSettledCash(PaymentFact payment) =>
        !string.IsNullOrWhiteSpace(payment.Method)
        && !string.Equals(payment.Method, "account_credit", StringComparison.OrdinalIgnoreCase)
        && string.Equals(payment.SettlementStatus, "settled", StringComparison.OrdinalIgnoreCase);

    private static bool IsMonthInRange(int year, int month, DateOnly from, DateOnly to)
    {
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        return periodStart <= to && periodEnd >= from;
    }

    private sealed record PaymentFact(decimal Amount, string? Method, string? SettlementStatus);
}
