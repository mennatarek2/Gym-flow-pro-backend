namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Activities;

public interface ISessionBookingService
{
    Task<Result<List<SessionDto>>> GetSessionsByDateAsync(Guid tenantId, DateOnly date, CancellationToken ct = default);
    Task<Result<SessionDetailDto>> GetSessionDetailAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default);
    Task<Result<BookingDto>> CreateBookingAsync(Guid tenantId, CreateBookingRequest request, Guid? staffUserId, CancellationToken ct = default);

    /// <summary>Staff cancellation. Same late-cancel rule; staff may cancel even past the window (still marked late).</summary>
    Task<Result<BookingDto>> CancelBookingAsync(Guid tenantId, Guid bookingId, CancellationToken ct = default);

    /// <summary>Member self-cancellation — enforces ownership and the late-cancel quota rule.</summary>
    Task<Result<BookingDto>> CancelOwnBookingAsync(Guid tenantId, Guid memberId, Guid bookingId, CancellationToken ct = default);

    /// <summary>Cancellation policy for the member app before confirming ("cancel before 6:00 PM to restore your credit").</summary>
    Task<Result<MemberCancelPolicyDto>> GetCancelPolicyAsync(Guid tenantId, Guid bookingId, CancellationToken ct = default);

    Task<Result<BookingDto>> CheckInBookingAsync(Guid tenantId, Guid bookingId, Guid staffUserId, CancellationToken ct = default);
}
