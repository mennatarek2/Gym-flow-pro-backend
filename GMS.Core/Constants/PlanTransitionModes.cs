namespace GMS.Core.Constants;

/// <summary>Desk renew / plan-change transition modes (no forced day rollover).</summary>
public static class PlanTransitionModes
{
    public const string CancelAndSwitch = "cancel_and_switch";
    public const string QueueNext = "queue_next";
    public const string ManualRollover = "manual_rollover";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        CancelAndSwitch,
        QueueNext,
        ManualRollover
    };

    public static bool TryNormalize(string? raw, out string mode)
    {
        mode = string.IsNullOrWhiteSpace(raw)
            ? CancelAndSwitch
            : raw.Trim().ToLowerInvariant();
        return All.Contains(mode);
    }
}
