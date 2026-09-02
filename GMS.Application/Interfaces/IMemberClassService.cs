namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Activities;

/// <summary>
/// Read-only Member App class browsing. Does not create bookings or payments.
/// </summary>
public interface IMemberClassService
{
    Task<Result<List<MemberClassListItemDto>>> ListUpcomingAsync(
        Guid tenantId,
        Guid identityUserId,
        Guid? activityId = null,
        DateTime? fromUtc = null,
        int limit = 100,
        CancellationToken ct = default);

    Task<Result<MemberClassDetailsDto>> GetByIdAsync(
        Guid tenantId,
        Guid identityUserId,
        Guid sessionId,
        CancellationToken ct = default);
}
