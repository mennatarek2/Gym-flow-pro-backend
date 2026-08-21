namespace GMS.Core.Interfaces;

using GMS.Core.Entities;

/// <summary>
/// Domain repository for GymAttendance operations.
/// </summary>
public interface IAttendanceRepository
{
    /// <summary>
    /// Creates a new attendance check-in record.
    /// </summary>
    Task<GymAttendance> CreateCheckinAsync(GymAttendance attendance, CancellationToken ct = default);

    /// <summary>
    /// Gets all attendance records for today within a tenant.
    /// Includes member and membership navigation.
    /// </summary>
    Task<List<GymAttendance>> GetTodayAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets attendance history for a specific member with pagination.
    /// </summary>
    Task<List<GymAttendance>> GetMemberHistoryAsync(Guid memberId, Guid tenantId, int page = 1, int pageSize = 20, CancellationToken ct = default);

    /// <summary>
    /// Checks if member has already checked in today (prevents double check-in).
    /// </summary>
    Task<bool> HasCheckedInTodayAsync(Guid memberId, Guid tenantId, CancellationToken ct = default);
}
