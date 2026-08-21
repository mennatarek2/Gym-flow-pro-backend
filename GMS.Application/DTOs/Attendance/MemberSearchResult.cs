namespace GMS.Application.DTOs.Attendance;

/// <summary>
/// Search result item for member lookup.
/// Includes selectable flag for manual check-in UI.
/// </summary>
public class MemberSearchResult
{
    public Guid Id { get; set; }
    public string MemberNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string FullNameAr { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string ProfilePhotoUrl { get; set; } = string.Empty;

    /// <summary>Current membership status: active, scheduled, expired, frozen, cancelled, pending, none.</summary>
    public string MembershipStatus { get; set; } = "none";

    /// <summary>Current plan name (empty if no membership).</summary>
    public string PlanName { get; set; } = string.Empty;
    public string PlanNameAr { get; set; } = string.Empty;

    /// <summary>Plan type e.g. session_pack, monthly_unlimited.</summary>
    public string PlanType { get; set; } = string.Empty;

    /// <summary>Sessions left for session_pack plans; null otherwise.</summary>
    public int? SessionsRemaining { get; set; }

    /// <summary>
    /// Whether this member can be selected for manual check-in.
    /// False if expired, frozen, or inactive.
    /// </summary>
    public bool IsSelectable { get; set; }

    /// <summary>
    /// Reason why member cannot be selected (bilingual).
    /// Null if selectable.
    /// </summary>
    public string? UnselectableReason { get; set; }
    public string? UnselectableReasonAr { get; set; }
}
