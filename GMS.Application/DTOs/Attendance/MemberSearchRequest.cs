namespace GMS.Application.DTOs.Attendance;

/// <summary>
/// Request DTO for searching members (for manual check-in UI).
/// </summary>
public class MemberSearchRequest
{
    /// <summary>
    /// Search query — matches against name, phone, or member number.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Whether to include inactive members in results.
    /// </summary>
    public bool IncludeInactive { get; set; } = false;
}
