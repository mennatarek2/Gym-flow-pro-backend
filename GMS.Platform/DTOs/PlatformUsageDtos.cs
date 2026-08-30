namespace GMS.Platform.DTOs;

/// <summary>Platform-wide usage rollup for the current Cairo billing period — the operational
/// counterpart to the per-tenant UsageCounters already returned in PlatformTenantDetailDto.</summary>
public class PlatformUsageSummaryDto
{
    /// <summary>YYYY-MM (Cairo), same period key as platform.usage_counters.</summary>
    public string Period { get; set; } = string.Empty;
    public List<UsageMetricTotalDto> Totals { get; set; } = new();
    /// <summary>Tenants at or above 80% of their cap for any metric, worst first.</summary>
    public List<TenantNearLimitDto> TenantsNearLimit { get; set; } = new();
    public DateTime ComputedAtUtc { get; set; }
}

public class UsageMetricTotalDto
{
    public string Metric { get; set; } = string.Empty;
    public long TotalCount { get; set; }
    /// <summary>Number of tenants with a counter row for this metric this period (not a tenant count cap).</summary>
    public int TenantCount { get; set; }
}

public class TenantNearLimitDto
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public int Count { get; set; }
    public int Cap { get; set; }
    /// <summary>Rounded percentage, e.g. 92 for 92%.</summary>
    public int PercentOfCap { get; set; }
}
