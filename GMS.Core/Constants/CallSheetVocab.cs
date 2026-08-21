namespace GMS.Core.Constants;

public static class CallSheetVocab
{
    public const string SourceSystem = "system";
    public const string SourceManual = "manual";

    public static readonly string[] Reasons =
        { "renewal", "trial", "payment", "welcome", "inactive", "offer", "custom" };

    public static readonly string[] Priorities = { "high", "medium", "low" };

    public static readonly string[] Statuses =
        { "pending", "in_progress", "contacted", "no_answer", "completed", "cancelled" };

    public static readonly string[] OpenStatuses =
        { "pending", "in_progress", "contacted", "no_answer" };

    /// <summary>Write vocabulary plus legacy CallOutcome values.</summary>
    public static readonly string[] Outcomes =
    {
        "reached", "no_answer", "busy", "wrong_number", "not_interested",
        "will_visit", "renewed", "needs_follow_up",
        "contacted", "declined"
    };

    public static readonly string[] NextActions =
    {
        "call_tomorrow", "call_in_3_days", "member_will_visit", "member_renewed",
        "not_interested", "wrong_number", "no_answer", "completed", "custom"
    };

    public static bool IsOpen(string? status) =>
        OpenStatuses.Contains((status ?? "").Trim().ToLowerInvariant());

    public static string NormalizeOutcome(string outcome)
    {
        var v = (outcome ?? "").Trim().ToLowerInvariant();
        return v switch
        {
            "contacted" => "reached",
            "declined" => "not_interested",
            _ => v
        };
    }
}
