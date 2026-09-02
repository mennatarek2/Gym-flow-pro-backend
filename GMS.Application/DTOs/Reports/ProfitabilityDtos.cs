namespace GMS.Application.DTOs.Reports;

/// <summary>
/// Canonical financial report. Payment, revenue, cash flow, and profitability are
/// deliberately exposed as separate concepts.
/// </summary>
public sealed class ProfitabilityDto
{
    public string CalculationVersion { get; init; } = "financial-v1";
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }

    public decimal Collections { get; init; }
    public decimal SettledCashInflow { get; init; }
    public bool SettledCashAvailable { get; init; }
    public decimal Revenue { get; init; }
    public decimal RevenueAdjustments { get; init; }
    public decimal Refunds { get; init; }
    public decimal CashRefunds { get; init; }
    public decimal CreditRefunds { get; init; }
    public decimal? Cogs { get; init; }
    public decimal OperatingExpenses { get; init; }
    public decimal? PayrollExpense { get; init; }
    public decimal PayrollCashDisbursements { get; init; }
    public decimal SupplierCashPayments { get; init; }
    public decimal? GrossProfit { get; init; }
    public decimal? NetProfit { get; init; }
    public bool NetProfitAvailable { get; init; }
    public decimal? ProfitMargin { get; init; }
    public decimal CashOutflows { get; init; }
    public decimal NetCashFlow { get; init; }
    public bool CashFlowAvailable { get; init; }
    public bool SupplierCashPaymentsAvailable { get; init; }
    public decimal AccountsReceivable { get; init; }
    public int AccountsReceivableCount { get; init; }
    public decimal AccountsPayable { get; init; }

    public bool CogsAvailable { get; init; }
    public bool PayrollAvailable { get; init; }
    public string PayrollCoverageStatus { get; init; } = "NO_PAYROLL_PERIOD";
    public bool AccountsPayableAvailable { get; init; }
    public List<string> DataIssues { get; init; } = new();
    public Dictionary<string, string> TrustStates { get; init; } = new();
    public List<ProfitabilityBreakdownDto> RevenueBreakdown { get; init; } = new();
    public List<ProfitabilityTrendPointDto> RevenueTrend { get; init; } = new();
}

public sealed class ProfitabilityBreakdownDto
{
    public string Key { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public int Count { get; init; }
}

public sealed class ProfitabilityTrendPointDto
{
    public DateOnly Date { get; init; }
    public decimal Value { get; init; }
}

public sealed class CashFlowDto
{
    public string CalculationVersion { get; init; } = "financial-v1";
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public decimal Collections { get; init; }
    public decimal SettledCashInflow { get; init; }
    public bool SettledCashAvailable { get; init; }
    public decimal CashRefunds { get; init; }
    public decimal OperatingExpenseCashOutflows { get; init; }
    public decimal PayrollCashDisbursements { get; init; }
    public decimal SupplierCashPayments { get; init; }
    public decimal CashOutflows { get; init; }
    public decimal NetCashFlow { get; init; }
    public bool CashFlowAvailable { get; init; }
    public bool SupplierCashPaymentsAvailable { get; init; }
    public string PayrollCoverageStatus { get; init; } = "NO_PAYROLL_PERIOD";
    public List<string> DataIssues { get; init; } = new();
}

public sealed class CogsBackfillDto
{
    public int Scanned { get; init; }
    public int Backfilled { get; init; }
    public int Skipped { get; init; }
    public List<Guid> SkippedSaleLineIds { get; init; } = new();
    public List<CogsBackfillItemDto> Items { get; init; } = new();
}

public sealed class CogsBackfillItemDto
{
    public Guid SaleLineId { get; init; }
    public decimal? OldCost { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public decimal? ReconstructedCost { get; init; }
    public string Status { get; init; } = string.Empty;
}
