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

/// <summary>HR Phase 3: staff check-in/check-out and attendance history. Separate from member
/// <see cref="AttendanceController"/> (gym check-in) — this tracks working hours, not gym visits.</summary>
[Route("api/hr/employee-attendance")]
[Authorize]
[FeatureFlag("hr")]
public class HrEmployeeAttendanceController : BaseApiController
{
    private readonly IEmployeeAttendanceService _attendance;
    private readonly ITenantContext _tenantContext;

    public HrEmployeeAttendanceController(IEmployeeAttendanceService attendance, ITenantContext tenantContext)
    {
        _attendance = attendance;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.HrAttendanceView)]
    [ProducesResponseType(typeof(List<EmployeeAttendanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? employeeId = null, [FromQuery] string? status = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _attendance.ListAsync(_tenantContext.TenantId, from, to, employeeId, status);
        return Ok(result.Data);
    }

    [HttpPost("check-in")]
    [HasPermission(Permissions.HrAttendanceManage)]
    [ProducesResponseType(typeof(EmployeeAttendanceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckIn([FromBody] CheckInRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        if (request.EmployeeId == null)
            return BadRequest(new { error = "employeeId is required / معرف الموظف مطلوب" });

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var source = User.IsInRole("Receptionist") ? AttendanceSources.Reception : AttendanceSources.Manual;
        var result = await _attendance.CheckInAsync(_tenantContext.TenantId, request.EmployeeId.Value, request.Notes, source, actorAppUserId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("check-out")]
    [HasPermission(Permissions.HrAttendanceManage)]
    [ProducesResponseType(typeof(EmployeeAttendanceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckOut([FromBody] CheckOutRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        if (request.EmployeeId == null)
            return BadRequest(new { error = "employeeId is required / معرف الموظف مطلوب" });

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var result = await _attendance.CheckOutAsync(_tenantContext.TenantId, request.EmployeeId.Value, actorAppUserId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.HrAttendanceManage)]
    [ProducesResponseType(typeof(EmployeeAttendanceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Correct(Guid id, [FromBody] CorrectAttendanceRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var result = await _attendance.CorrectAsync(_tenantContext.TenantId, id, request, actorAppUserId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(List<EmployeeAttendanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var employeeId = await ResolveOwnEmployeeIdAsync();
        if (employeeId == null)
            return Forbid();

        var result = await _attendance.ListAsync(_tenantContext.TenantId, from, to, employeeId);
        return Ok(result.Data);
    }

    [HttpPost("me/check-in")]
    [ProducesResponseType(typeof(EmployeeAttendanceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckInMine([FromBody] CheckInRequest? request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var employeeId = await ResolveOwnEmployeeIdAsync();
        if (employeeId == null)
            return Forbid();

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var result = await _attendance.CheckInAsync(_tenantContext.TenantId, employeeId.Value, request?.Notes, AttendanceSources.Employee, actorAppUserId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("me/check-out")]
    [ProducesResponseType(typeof(EmployeeAttendanceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckOutMine()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var employeeId = await ResolveOwnEmployeeIdAsync();
        if (employeeId == null)
            return Forbid();

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var result = await _attendance.CheckOutAsync(_tenantContext.TenantId, employeeId.Value, actorAppUserId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    private Task<Guid?> ResolveOwnEmployeeIdAsync()
    {
        var identityUserId = GetIdentityUserId();
        return identityUserId == Guid.Empty
            ? Task.FromResult<Guid?>(null)
            : _attendance.ResolveEmployeeIdForCallerAsync(_tenantContext.TenantId, identityUserId);
    }

    private Task<Guid?> ResolveActingAppUserIdAsync()
    {
        var identityUserId = GetIdentityUserId();
        return identityUserId == Guid.Empty
            ? Task.FromResult<Guid?>(null)
            : _attendance.ResolveAppUserIdForCallerAsync(_tenantContext.TenantId, identityUserId);
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
