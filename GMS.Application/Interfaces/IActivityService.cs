namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Activities;

public interface IActivityService
{
    Task<Result<List<ActivityDto>>> ListAsync(Guid tenantId, CancellationToken ct = default);
    Task<Result<ActivityDto>> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<Result<ActivityDto>> CreateAsync(Guid tenantId, CreateActivityRequest request, CancellationToken ct = default);
    Task<Result<ActivityDto>> UpdateAsync(Guid tenantId, Guid id, UpdateActivityRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<Result<List<ActivityScheduleDto>>> ListSchedulesAsync(Guid tenantId, Guid activityId, CancellationToken ct = default);
    Task<Result<ActivityScheduleDto>> CreateScheduleAsync(Guid tenantId, Guid activityId, CreateScheduleRequest request, CancellationToken ct = default);
    Task<Result> DeleteScheduleAsync(Guid tenantId, Guid scheduleId, CancellationToken ct = default);
}
