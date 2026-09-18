namespace GMS.Core.Constants;

/// <summary>
/// Physical PVC access-card lifecycle statuses.
/// Only <see cref="Assigned"/> authenticates at desk barcode check-in.
/// </summary>
public static class AccessCardStatuses
{
    public const string Available = "Available";
    public const string Assigned = "Assigned";
    public const string Lost = "Lost";
    public const string Damaged = "Damaged";
    public const string Blocked = "Blocked";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Available, Assigned, Lost, Damaged, Blocked
    };

    public static readonly IReadOnlyList<string> Terminal = new[]
    {
        Lost, Damaged, Blocked
    };

    public static bool IsKnown(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && All.Contains(status, StringComparer.OrdinalIgnoreCase);

    public static bool IsTerminal(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && Terminal.Contains(status, StringComparer.OrdinalIgnoreCase);
}
