namespace GMS.Application.DTOs.Analytics;

/// <summary>
/// Dashboard overview with key KPIs from pre-computed snapshots.
/// </summary>
public class DashboardOverviewDto
{
    public int ActiveMembers { get; set; }
    public int ExpiredMembers { get; set; }
    public int NewMembersThisMonth { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public int CheckinsToday { get; set; }
    public int CheckinsThisWeek { get; set; }
    public DateTime SnapshotTimeUtc { get; set; }
}
