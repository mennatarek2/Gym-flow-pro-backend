namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

public interface IPlatformUsageService
{
    /// <summary>Cross-tenant rollup for the current Cairo period, built entirely from the same
    /// platform.usage_counters rows the nightly rollup job already writes — no new usage-tracking
    /// mechanism, just a read across tenants instead of one.</summary>
    Task<PlatformUsageSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
