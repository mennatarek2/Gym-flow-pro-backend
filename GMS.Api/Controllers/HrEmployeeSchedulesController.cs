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

/// <summary>HR Phase 3: assigning employees to shift templates by date.</summary>
[Route("api/hr/employee-schedules")]
[Authorize]
[FeatureFlag("hr")]
public class HrEmployeeSchedulesController : BaseApiController
{
    private readonly IEmployeeScheduleService _schedules;
    private readonly IEmployeeAttendanceService _attendance;
    private readonly ITenantContext _tenantContext;

    public HrEmployeeSchedulesController(
        IEmployeeScheduleService schedules, IEmployeeAttendanceService attendance, ITenantContext tenantContext)
    {
        _schedules = schedules;
        _attendance = attendance;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.HrAttendanceView)]
    [ProducesResponseType(typeof(List<EmployeeScheduleAssignmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? employeeId = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _schedules.ListAsync(_tenantContext.TenantId, from, to, employeeId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.HrShiftsManage)]
    [ProducesResponseType(typeof(EmployeeScheduleAssignmentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Assign([FromBody] AssignScheduleRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _schedules.AssignAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Created(string.Empty, result.Data);
    }

    [HttpDelete("{employeeId:guid}/{date}")]
    [HasPermission(Permissions.HrShiftsManage)]
    public async Task<IActionResult> Remove(Guid employeeId, DateOnly date)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _schedules.RemoveAsync(_tenantContext.TenantId, employeeId, date);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return NoContent();
    }

    [HttpPost("bulk")]
    [HasPermission(Permissions.HrShiftsManage)]
    [ProducesResponseType(typeof(BulkAssignResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkAssign([FromBody] BulkAssignScheduleRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _schedules.BulkAssignAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(List<EmployeeScheduleAssignmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var identityUserId = GetIdentityUserId();
        if (identityUserId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var employeeId = await _attendance.ResolveEmployeeIdForCallerAsync(_tenantContext.TenantId, identityUserId);
        if (employeeId == null)
            return Forbid();

        var result = await _schedules.ListAsync(_tenantContext.TenantId, from, to, employeeId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
