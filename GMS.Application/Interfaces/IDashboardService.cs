namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Dashboard;

public interface IDashboardService
{
    Task<Result<DashboardOverviewDto>> GetOverviewAsync(
        Guid tenantId,
        DashboardQuery query,
        DashboardAccessContext access,
        CancellationToken ct = default);
}
