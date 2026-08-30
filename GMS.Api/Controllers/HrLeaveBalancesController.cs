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

/// <summary>HR Phase 4: per-employee, per-year leave balances.</summary>
[Route("api/hr/leave-balances")]
[Authorize]
[FeatureFlag("hr")]
public class HrLeaveBalancesController : BaseApiController
{
    private readonly ILeaveBalanceService _balances;
    private readonly IEmployeeService _employees;
    private readonly ITenantContext _tenantContext;

    public HrLeaveBalancesController(ILeaveBalanceService balances, IEmployeeService employees, ITenantContext tenantContext)
    {
        _balances = balances;
        _employees = employees;
        _tenantContext = tenantContext;
    }

    [HttpGet("{employeeId:guid}")]
    [HasPermission(Permissions.HrLeaveView)]
    [ProducesResponseType(typeof(List<LeaveBalanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid employeeId, [FromQuery] int? year = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _balances.ListAsync(_tenantContext.TenantId, employeeId, year);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPut("{employeeId:guid}/{leaveType}/{year:int}")]
    [HasPermission(Permissions.HrLeaveManage)]
    [ProducesResponseType(typeof(LeaveBalanceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetEntitlement(Guid employeeId, string leaveType, int year, [FromBody] SetLeaveEntitlementRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _balances.SetEntitlementAsync(_tenantContext.TenantId, employeeId, leaveType, year, request.EntitledDays);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(List<LeaveBalanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine([FromQuery] int? year = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var identityUserId))
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var employeeId = await _employees.ResolveEmployeeIdForCallerAsync(_tenantContext.TenantId, identityUserId);
        if (employeeId == null)
            return Forbid();

        var result = await _balances.ListAsync(_tenantContext.TenantId, employeeId.Value, year);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }
}

public class SetLeaveEntitlementRequest
{
    public decimal EntitledDays { get; set; }
}
