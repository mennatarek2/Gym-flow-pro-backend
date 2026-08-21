namespace GMS.Application.DTOs.Attendance;

using GMS.Core.Enums;

/// <summary>
/// Request DTO for manual check-in by staff.
/// Requires ManagerOrAbove policy.
/// </summary>
public class ManualCheckinRequest
{
    /// <summary>
    /// The member to check in.
    /// </summary>
    public Guid MemberId { get; set; }

    /// <summary>
    /// Reason for manual check-in.
    /// </summary>
    public ManualCheckinReason Reason { get; set; }

    /// <summary>
    /// Optional notes for 'Other' reason.
    /// </summary>
    public string? Notes { get; set; }
}
