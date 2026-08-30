namespace GMS.Application.Interfaces;

using GMS.Application.Common;

public interface ISessionGenerationService
{
    /// <summary>Idempotently materializes sessions from active schedules for the rolling window. Returns created count.</summary>
    Task<int> GenerateUpcomingSessionsAsync(Guid tenantId, int? windowDaysOverride = null, CancellationToken ct = default);

    /// <summary>Marks elapsed sessions completed and un-checked-in booked members as no-show. Returns affected bookings.</summary>
    Task<int> FinalizeElapsedSessionsAsync(Guid tenantId, CancellationToken ct = default);
}
