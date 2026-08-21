namespace GMS.Application.DTOs.Analytics;

/// <summary>
/// Trial funnel for a cohort of trials issued in a given month (Issued), evaluated against each
/// member's current TrialOutcome (Converted/Expired may reflect outcomes reached after that month).
/// </summary>
public class TrialAnalyticsDto
{
    public int Issued { get; set; }
    public int Converted { get; set; }
    public decimal ConversionRate { get; set; }
    public int Expired { get; set; }
}
