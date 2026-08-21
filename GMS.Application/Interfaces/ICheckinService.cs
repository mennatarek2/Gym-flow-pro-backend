namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Attendance;

/// <summary>
/// Service for QR and manual check-in operations.
/// </summary>
public interface ICheckinService
{
    /// <summary>
    /// Processes a QR code check-in from a member's mobile app.
    /// Runs the full validation gauntlet before creating an attendance record.
    /// Target: complete in under 300ms with warm cache.
    /// </summary>
    Task<Result<QrCheckinResponse>> ProcessQrCheckinAsync(QrCheckinRequest request, Guid userId, Guid tenantId);

    /// <summary>
    /// Processes a manual check-in initiated by staff.
    /// Same validation gauntlet but entry_method='manual' with staff_user_id.
    /// </summary>
    Task<Result<ManualCheckinResponse>> ProcessManualCheckinAsync(ManualCheckinRequest request, Guid staffUserId, Guid tenantId);

    /// <summary>
    /// Desk barcode check-in (access card). Exact MemberNumber under tenant; EntryMethod=barcode.
    /// Same membership gauntlet as manual check-in (MAC-P0 / C7).
    /// </summary>
    Task<Result<ManualCheckinResponse>> ProcessBarcodeCheckinAsync(BarcodeCheckinRequest request, Guid staffUserId, Guid tenantId);

    /// <summary>
    /// Searches members for the manual check-in UI.
    /// Returns results with selectable flag (expired/frozen members shown but not selectable).
    /// </summary>
    Task<Result<List<MemberSearchResult>>> SearchMembersForCheckinAsync(MemberSearchRequest request, Guid tenantId);

    /// <summary>
    /// Gets today's attendance records with optional entry method filter.
    /// </summary>
    Task<Result<List<TodayAttendanceDto>>> GetTodayAttendanceAsync(Guid tenantId, string filter = "all");
}
