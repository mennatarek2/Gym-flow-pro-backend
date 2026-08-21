namespace GMS.Core.Constants;

/// <summary>Operational member-store order lifecycle (Stage 0 — no payment / no stock write).</summary>
public static class MemberOrderStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Ready = "ready";
    public const string Completed = "completed";
    public const string Rejected = "rejected";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Pending, Accepted, Ready, Completed, Rejected
    };
}
