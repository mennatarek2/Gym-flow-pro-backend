namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

public interface IDeskFeedbackService
{
    Task<DeskFeedbackDto> SubmitAsync(SubmitDeskFeedbackRequest request, DeskFeedbackActorContext actor, CancellationToken ct = default);
    /// <summary>License-key + installation authenticated ingest from Local. Links CustomerId from the license when present.</summary>
    Task<DeskFeedbackDto> SubmitFromLocalLicenseAsync(IngestLocalDeskFeedbackRequest request, CancellationToken ct = default);
    Task<List<DeskFeedbackDto>> ListAsync(Guid? customerId, Guid? tenantId, string? category, string? status, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<DeskFeedbackDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<DeskFeedbackDto?> UpdateAsync(Guid id, UpdateDeskFeedbackRequest request, Guid actorPlatformUserId, CancellationToken ct = default);
}
