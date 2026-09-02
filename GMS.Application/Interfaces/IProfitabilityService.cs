namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Reports;

public interface IProfitabilityService
{
    Task<Result<ProfitabilityDto>> GetAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);

    Task<Result<CogsBackfillDto>> BackfillCogsAsync(
        Guid tenantId,
        CancellationToken ct = default);
}
