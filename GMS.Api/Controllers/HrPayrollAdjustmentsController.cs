namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>HR Phase 5: manual bonus/allowance/overtime/deduction inputs for a payroll period.</summary>
[Route("api/hr/payroll-periods/{periodId:guid}/adjustments")]
[Authorize]
[FeatureFlag("hr")]
public class HrPayrollAdjustmentsController : BaseApiController
{
    private readonly IPayrollAdjustmentService _adjustments;
    private readonly IEmployeeService _employees;
    private readonly ITenantContext _tenantContext;

    public HrPayrollAdjustmentsController(IPayrollAdjustmentService adjustments, IEmployeeService employees, ITenantContext tenantContext)
    {
        _adjustments = adjustments;
        _employees = employees;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.HrPayrollView)]
    [ProducesResponseType(typeof(List<PayrollAdjustmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid periodId, [FromQuery] Guid? employeeId = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _adjustments.ListAsync(_tenantContext.TenantId, periodId, employeeId);
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.HrPayrollManage)]
    [ProducesResponseType(typeof(PayrollAdjustmentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid periodId, [FromBody] CreatePayrollAdjustmentRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        Guid? actorAppUserId = null;
        if (Guid.TryParse(sub, out var identityUserId))
            actorAppUserId = await _employees.ResolveAppUserIdForCallerAsync(_tenantContext.TenantId, identityUserId);

        var result = await _adjustments.CreateAsync(_tenantContext.TenantId, periodId, request, actorAppUserId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(List), new { periodId }, result.Data);
    }
}
