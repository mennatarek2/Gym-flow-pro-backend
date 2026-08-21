namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Plans;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// REST API controller for membership plan management.
/// All operations are automatically scoped to the current tenant.
/// Supports 5 plan types: monthly_unlimited, session_pack, time_limited, pt_credits, family.
/// </summary>
[Route("api/membership-plans")]
[Authorize]
public class MembershipPlansController : BaseApiController
{
    private readonly IMembershipPlanService _planService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<MembershipPlansController> _logger;

    public MembershipPlansController(
        IMembershipPlanService planService,
        ITenantContext tenantContext,
        ILogger<MembershipPlansController> logger)
    {
        _planService = planService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all active membership plans for the current tenant.
    /// GET /api/membership-plans
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(List<PlanListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPlans()
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _planService.GetPlansAsync(tenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error!);
    }

    /// <summary>
    /// Get full details of a specific membership plan including membership count.
    /// GET /api/membership-plans/{id}
    /// </summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(PlanDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlan(Guid id)
    {
        var result = await _planService.GetPlanByIdAsync(id);
        return result.IsSuccess ? Ok(result.Data) : NotFound(result.Error!);
    }

    /// <summary>
    /// Create a new membership plan.
    /// Validates plan type specific requirements:
    /// - session_pack: SessionCount must be 10, 20, or 50
    /// - time_limited: TimeRestrictionStart and End must be provided
    /// 
    /// POST /api/membership-plans
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(PlanDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePlan([FromBody] CreatePlanRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _planService.CreatePlanAsync(tenantId, request);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to create plan: {Error}", result.Error);
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation("Membership plan created: {PlanId}", result.Data!.Id);

        return CreatedAtAction(
            nameof(GetPlan),
            new { id = result.Data!.Id },
            result.Data);
    }

    /// <summary>
    /// Update an existing membership plan.
    /// PUT /api/membership-plans/{id}
    /// </summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(PlanDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] UpdatePlanRequest request)
    {
        var result = await _planService.UpdatePlanAsync(id, request);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to update plan {PlanId}: {Error}", id, result.Error);
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation("Membership plan updated: {PlanId}", id);
        return Ok(result.Data);
    }

    /// <summary>
    /// Delete (soft delete) a membership plan.
    /// Returns 409 Conflict if there are active memberships on this plan.
    /// 
    /// DELETE /api/membership-plans/{id}
    /// </summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeletePlan(Guid id)
    {
        var result = await _planService.DeletePlanAsync(id);

        if (!result.IsSuccess)
        {
            // If plan has active members, return 409 Conflict instead of 400
            if (result.Error?.Contains("active memberships") ?? false)
            {
                _logger.LogWarning("Cannot delete plan with active memberships: {PlanId}", id);
                return Conflict(new { error = result.Error, message = result.Message });
            }

            _logger.LogWarning("Failed to delete plan {PlanId}: {Error}", id, result.Error);
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        _logger.LogInformation("Membership plan deleted: {PlanId}", id);
        return Ok(new { message = result.Message ?? "Plan deleted successfully / تم حذف الخطة بنجاح" });
    }
}
