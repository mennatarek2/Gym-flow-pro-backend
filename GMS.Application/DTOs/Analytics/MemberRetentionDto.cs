namespace GMS.Application.DTOs.Analytics;

/// <summary>
/// Member retention analysis.
/// </summary>
public class MemberRetentionDto
{
    public int TotalExpiredMemberships { get; set; }
    public int RenewedMemberships { get; set; }
    public decimal RetentionRate { get; set; } // Percentage (0-100)
}
