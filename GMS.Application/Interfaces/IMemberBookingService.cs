namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Activities;

/// <summary>Member App booking flows — all scoped to the authenticated member.</summary>
public interface IMemberBookingService
{
    Task<Result<List<MemberActivityDto>>> ListActivitiesAsync(Guid tenantId, Guid identityUserId, CancellationToken ct = default);
    Task<Result<List<MemberSessionDto>>> ListUpcomingSessionsAsync(Guid tenantId, Guid identityUserId, Guid? activityId, DateTime? fromUtc, CancellationToken ct = default);
    Task<Result<MemberBookingDto>> BookAsync(Guid tenantId, Guid identityUserId, Guid sessionId, CancellationToken ct = default);

    /// <summary>Reception books a drop-in customer who already paid for this specific session (sale id required).</summary>
    Task<Result<MemberBookingDto>> BookWithSaleAsync(Guid tenantId, Guid memberId, Guid sessionId, Guid saleId, CancellationToken ct = default);
    Task<Result<MemberCancelPolicyDto>> GetCancelPolicyAsync(Guid tenantId, Guid identityUserId, Guid bookingId, CancellationToken ct = default);
    Task<Result<MemberBookingDto>> CancelOwnAsync(Guid tenantId, Guid identityUserId, Guid bookingId, CancellationToken ct = default);
    Task<Result<List<MemberBookingDto>>> MyBookingsAsync(Guid tenantId, Guid identityUserId, CancellationToken ct = default);
    Task<Result<MemberBookingDto>> MyBookingAsync(Guid tenantId, Guid identityUserId, Guid bookingId, CancellationToken ct = default);
}
