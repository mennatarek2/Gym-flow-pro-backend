namespace GMS.Application.DTOs.Analytics;

/// <summary>
/// Revenue chart data for the last 6 months.
/// </summary>
public class RevenueChartDto
{
    public List<string> Labels { get; set; } = new();  // Month names (Jan, Feb, etc.)
    public List<decimal> Values { get; set; } = new(); // Revenue values
}
