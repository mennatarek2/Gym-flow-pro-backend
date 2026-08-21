namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

/// <summary>CP8 read-side SaaS metrics for Platform Console / investor views.</summary>
public interface IPlatformMetricsService
{
    Task<MrrSnapshotDto> GetMrrAsync(DateOnly? asOf, CancellationToken cancellationToken = default);
    Task<MrrMovementDto> GetMovementAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<ChurnMetricsDto> GetChurnAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<ConversionMetricsDto> GetConversionAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<TierDistributionDto> GetTierDistributionAsync(DateOnly? asOf, CancellationToken cancellationToken = default);
}
