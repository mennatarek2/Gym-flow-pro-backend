namespace GMS.Platform.DTOs;

/// <summary>CP8 SaaS metrics — snapshot as-of / range payloads for Platform Console.</summary>
public class MrrSnapshotDto
{
    public DateOnly AsOf { get; set; }
    public decimal MrrEgp { get; set; }
    public decimal ArrEgp { get; set; }
    public int PayingTenantCount { get; set; }
    public DateTime ComputedAtUtc { get; set; }
    public string Currency { get; set; } = "EGP";
}

public class MrrMovementDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal StartingMrrEgp { get; set; }
    public decimal NewMrrEgp { get; set; }
    public decimal ExpansionMrrEgp { get; set; }
    public decimal ContractionMrrEgp { get; set; }
    public decimal ChurnedMrrEgp { get; set; }
    /// <summary>Starting + new + expansion − contraction − churned.</summary>
    public decimal EndingMrrEgp { get; set; }
    /// <summary>Direct recomputation of MRR at <see cref="To"/> (should match Ending within rounding).</summary>
    public decimal EndingMrrDirectEgp { get; set; }
    public bool Reconciles { get; set; }
    public DateTime ComputedAtUtc { get; set; }
}

public class ChurnMetricsDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    /// <summary>Churned MRR / starting MRR (0–1).</summary>
    public decimal GrossChurnRate { get; set; }
    public decimal StartingMrrEgp { get; set; }
    public decimal ChurnedMrrEgp { get; set; }
    public int StartingPayingTenants { get; set; }
    public int ChurnedTenants { get; set; }
    public List<CohortRetentionDto> Cohorts { get; set; } = new();
    public DateTime ComputedAtUtc { get; set; }
}

public class CohortRetentionDto
{
    /// <summary>Signup month (yyyy-MM) from dbo.tenants.CreatedAtUtc (Cairo month).</summary>
    public string CohortMonth { get; set; } = string.Empty;
    public int SignedUp { get; set; }
    /// <summary>Still paying (active/past_due counting toward MRR) as of period end.</summary>
    public int RetainedPaying { get; set; }
    public decimal RetentionRate { get; set; }
}

public class ConversionMetricsDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int TrialsStarted { get; set; }
    public int ConvertedToPaid { get; set; }
    /// <summary>Converted / trials (0–1).</summary>
    public decimal ConversionRate { get; set; }
    public DateTime ComputedAtUtc { get; set; }
}

public class TierDistributionDto
{
    public DateOnly AsOf { get; set; }
    public List<TierDistributionRowDto> Tiers { get; set; } = new();
    public decimal TotalMrrEgp { get; set; }
    public int TotalPayingTenants { get; set; }
    public DateTime ComputedAtUtc { get; set; }
}

public class TierDistributionRowDto
{
    public string PlanTier { get; set; } = string.Empty;
    public int TenantCount { get; set; }
    public decimal MrrEgp { get; set; }
}
