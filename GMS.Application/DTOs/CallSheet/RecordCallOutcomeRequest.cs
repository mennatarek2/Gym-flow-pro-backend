namespace GMS.Application.DTOs.CallSheet;

/// <summary>Request body for POST /api/call-sheet/{followUpId}/outcome.</summary>
public class RecordCallOutcomeRequest
{
    /// <summary>reached | no_answer | busy | wrong_number | not_interested | will_visit | renewed | needs_follow_up</summary>
    public string Outcome { get; set; } = string.Empty;

    public string? Note { get; set; }

    /// <summary>call_tomorrow | call_in_3_days | member_will_visit | member_renewed | not_interested | wrong_number | no_answer | completed | custom</summary>
    public string? NextAction { get; set; }

    public DateTime? NextActionAtUtc { get; set; }
}
