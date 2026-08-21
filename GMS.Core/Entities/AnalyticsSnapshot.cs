namespace GMS.Core.Entities;

/// <summary>
/// Daily snapshot of analytics KPIs for dashboard performance.
/// Pre-computed by AnalyticsAggregationJob (Hangfire) every night.
/// </summary>
public class AnalyticsSnapshot : BaseEntity
{
    // Tenant context
    public Guid TenantId { get; set; }

    // Snapshot date
    public DateOnly SnapshotDate { get; set; }

    // Membership metrics
    public int ActiveMembers { get; set; }
    public int ExpiredMembers { get; set; }
    public int NewMembersThisMonth { get; set; }

    // Revenue metrics
    public decimal RevenueThisMonth { get; set; }

    // Attendance metrics
    public int CheckinsToday { get; set; }
    public int CheckinsThisWeek { get; set; }

    // Insights
    public Guid? TopPlanId { get; set; }

    // Timestamps
    public DateTime CreatedAtUtc { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public MembershipPlan? TopPlan { get; set; }
}
