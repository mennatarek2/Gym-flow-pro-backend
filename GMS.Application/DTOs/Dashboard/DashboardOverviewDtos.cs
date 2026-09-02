namespace GMS.Application.DTOs.Dashboard;

/// <summary>
/// Role-aware gym dashboard payload. Null sections/fields mean the caller is
/// not entitled to that data or the underlying capability is unavailable.
/// </summary>
public sealed class DashboardOverviewDto
{
    public DashboardPeriodDto Period { get; set; } = new();
    public DashboardTodayDto Today { get; set; } = new();
    public DashboardFinancialDto? Financial { get; set; }
    public DashboardBusinessDto? Business { get; set; }
    public DashboardOperationsDto Operations { get; set; } = new();
    public DashboardAttentionDto Attention { get; set; } = new();
    public List<DashboardQuickActionDto> QuickActions { get; set; } = new();
    public List<DashboardDataIssueDto> DataIssues { get; set; } = new();
}

public sealed class DashboardQuery
{
    public string Period { get; set; } = "month";
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed class DashboardAccessContext
{
    public string Role { get; init; } = string.Empty;
    public Guid? UserId { get; init; }
    public IReadOnlySet<string> Permissions { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    public bool Has(string permission) => Permissions.Contains(permission);
}

public sealed class DashboardPeriodDto
{
    public string Key { get; set; } = "month";
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
}

public sealed class DashboardTodayDto
{
    public decimal? RevenueToday { get; set; }
    public decimal? Outstanding { get; set; }
    public int? ActiveMembers { get; set; }
    public int? RenewalsDueSoon { get; set; }
    public int? CheckinsToday { get; set; }
    public int? CurrentlyInside { get; set; }
    public int? TodayClasses { get; set; }
    public int? UpcomingBookings { get; set; }
    public int? MyUpcomingClasses { get; set; }
    public int? TodayAttendance { get; set; }
    public int? ClassCapacityBooked { get; set; }
    public int? ClassCapacityTotal { get; set; }
}

public sealed class DashboardFinancialDto
{
    public string CalculationVersion { get; set; } = "financial-v1";
    public decimal CashCollected { get; set; }
    public decimal Collections { get; set; }
    public decimal SettledCashInflow { get; set; }
    public bool SettledCashAvailable { get; set; }
    public decimal Revenue { get; set; }
    public decimal RevenueAdjustments { get; set; }
    public decimal Refunds { get; set; }
    public decimal CashRefunds { get; set; }
    public decimal CreditRefunds { get; set; }
    public decimal Outstanding { get; set; }
    public decimal? Expenses { get; set; }
    public decimal? Cogs { get; set; }
    public decimal? GrossProfit { get; set; }
    public decimal? PayrollExpense { get; set; }
    public decimal? OperatingExpenses { get; set; }
    public decimal? NetProfit { get; set; }
    public decimal? ProfitMargin { get; set; }
    public decimal CashOutflows { get; set; }
    public decimal NetCashFlow { get; set; }
    public bool CashFlowAvailable { get; set; }
    public bool NetProfitAvailable { get; set; }
    public bool SupplierCashPaymentsAvailable { get; set; }
    public decimal AccountsReceivable { get; set; }
    public int AccountsReceivableCount { get; set; }
    public decimal AccountsPayable { get; set; }
    public bool CogsAvailable { get; set; }
    public bool PayrollAvailable { get; set; }
    public string PayrollCoverageStatus { get; set; } = "NO_PAYROLL_PERIOD";
    public List<string> FinancialDataIssues { get; set; } = new();
    public Dictionary<string, string> TrustStates { get; set; } = new();
    public List<DashboardRevenueBreakdownDto> Breakdown { get; set; } = new();
    public List<DashboardTrendPointDto> RevenueTrend { get; set; } = new();
}

public sealed class DashboardRevenueBreakdownDto
{
    public string Key { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Count { get; set; }
}

public sealed class DashboardBusinessDto
{
    public int ActiveMembers { get; set; }
    public int NewMembers { get; set; }
    public int Renewals { get; set; }
    public int Expired { get; set; }
    public int Inactive { get; set; }
    public int TrialsEndingSoon { get; set; }
}

public sealed class DashboardOperationsDto
{
    public int NearFullThresholdPercent { get; set; } = 80;
    public int CheckinsToday { get; set; }
    public int CurrentlyInside { get; set; }
    public int? MaxCapacity { get; set; }
    public int? AvailableCapacity { get; set; }
    public decimal? OccupancyPercent { get; set; }
    public List<DashboardTrendPointDto> AttendanceTrend { get; set; } = new();
    public List<DashboardSessionDto> Sessions { get; set; } = new();
}

public sealed class DashboardTrendPointDto
{
    public DateOnly Date { get; set; }
    public decimal Value { get; set; }
}

public sealed class DashboardSessionDto
{
    public Guid Id { get; set; }
    public string ActivityName { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Capacity { get; set; }
    public int BookedCount { get; set; }
    public int CheckedInCount { get; set; }
    public int RemainingCapacity { get; set; }
    public bool IsNearlyFull { get; set; }
    public bool IsMine { get; set; }
    public string? CoachName { get; set; }
    public List<DashboardBookingDto> Bookings { get; set; } = new();
}

public sealed class DashboardBookingDto
{
    public Guid Id { get; set; }
    public Guid? MemberId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool CheckedIn { get; set; }
}

public sealed class DashboardAttentionDto
{
    public List<DashboardAttentionItemDto> Items { get; set; } = new();
}

public sealed class DashboardAttentionItemDto
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal? Amount { get; set; }
}

public sealed class DashboardQuickActionDto
{
    public string Key { get; set; } = string.Empty;
}

public sealed class DashboardDataIssueDto
{
    public string Section { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
