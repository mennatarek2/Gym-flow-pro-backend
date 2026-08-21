namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Attendance;

public interface IGymOccupancyService
{
    Task<Result<GymOccupancyDto>> GetOccupancyAsync(Guid tenantId, CancellationToken ct = default);
}
