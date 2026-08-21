namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;
using GMS.Platform.Entities;

public interface IComputeTenantHealthScoresJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}

public interface ITenantHealthScoreService
{
    /// <summary>Score a single tenant (used by tests / on-demand). Persists the row.</summary>
    Task<TenantHealthScore> ScoreTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public interface IPlatformRiskQueueService
{
    Task<IReadOnlyList<RiskQueueItemDto>> ListAsync(
        string? bandCsv,
        CancellationToken cancellationToken = default);

    Task<PlatformActionResult> AssignAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        Guid? assigneePlatformUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<(PlatformActionResult Result, RiskQueueOutcomeDto? Outcome)> RecordOutcomeAsync(
        Guid tenantId,
        Guid actorPlatformUserId,
        RecordRiskQueueOutcomeRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
