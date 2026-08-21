namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Plans;

/// <summary>
/// Service interface for membership plan management.
/// </summary>
public interface IMembershipPlanService
{
    /// <summary>
    /// Get all active membership plans for the current tenant.
    /// </summary>
    Task<Result<List<PlanListItemDto>>> GetPlansAsync(Guid tenantId);

    /// <summary>
    /// Get detailed information about a specific membership plan.
    /// </summary>
    Task<Result<PlanDetailDto>> GetPlanByIdAsync(Guid id);

    /// <summary>
    /// Create a new membership plan.
    /// Validates plan type specific requirements (session count, time restrictions).
    /// </summary>
    Task<Result<PlanDetailDto>> CreatePlanAsync(Guid tenantId, CreatePlanRequest request);

    /// <summary>
    /// Update an existing membership plan.
    /// </summary>
    Task<Result<PlanDetailDto>> UpdatePlanAsync(Guid id, UpdatePlanRequest request);

    /// <summary>
    /// Delete (soft delete) a membership plan.
    /// Returns error if there are active memberships on this plan.
    /// </summary>
    Task<Result> DeletePlanAsync(Guid id);
}
