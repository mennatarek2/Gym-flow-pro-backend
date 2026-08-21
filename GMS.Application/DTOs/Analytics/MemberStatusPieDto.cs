namespace GMS.Application.DTOs.Analytics;

/// <summary>
/// Member status breakdown pie chart data.
/// </summary>
public class MemberStatusPieDto
{
    public int Active { get; set; }
    public int Expired { get; set; }
    public int Frozen { get; set; }
    public int Cancelled { get; set; }

    public int Total => Active + Expired + Frozen + Cancelled;
}
