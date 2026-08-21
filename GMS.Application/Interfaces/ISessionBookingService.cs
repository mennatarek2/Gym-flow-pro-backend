namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Activities;

public interface ISessionBookingService
{
    Task<Result<List<SessionDto>>> GetSessionsByDateAsync(Guid tenantId, DateOnly date, CancellationToken ct = default);
    Task<Result<SessionDetailDto>> GetSessionDetailAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default);
    Task<Result<BookingDto>> CreateBookingAsync(Guid tenantId, CreateBookingRequest request, Guid? staffUserId, CancellationToken ct = default);
    Task<Result<BookingDto>> CancelBookingAsync(Guid tenantId, Guid bookingId, CancellationToken ct = default);
    Task<Result<BookingDto>> CheckInBookingAsync(Guid tenantId, Guid bookingId, Guid staffUserId, CancellationToken ct = default);
}
