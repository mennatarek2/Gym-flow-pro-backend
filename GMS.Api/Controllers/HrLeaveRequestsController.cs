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

/// <summary>HR Phase 4: leave requests (create/approve/reject/cancel) and self-service.</summary>
[Route("api/hr/leave-requests")]
[Authorize]
[FeatureFlag("hr")]
public class HrLeaveRequestsController : BaseApiController
{
    private readonly ILeaveRequestService _leave;
    private readonly IEmployeeService _employees;
    private readonly ITenantContext _tenantContext;

    public HrLeaveRequestsController(ILeaveRequestService leave, IEmployeeService employees, ITenantContext tenantContext)
    {
        _leave = leave;
        _employees = employees;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.HrLeaveView)]
    [ProducesResponseType(typeof(List<LeaveRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? employeeId = null, [FromQuery] string? status = null, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _leave.ListAsync(_tenantContext.TenantId, employeeId, status, from, to);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.HrLeaveView)]
    [ProducesResponseType(typeof(LeaveRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _leave.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.HrLeaveManage)]
    [ProducesResponseType(typeof(LeaveRequestDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromQuery] Guid employeeId, [FromBody] CreateLeaveRequestRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _leave.CreateAsync(_tenantContext.TenantId, employeeId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.HrLeaveApprove)]
    [ProducesResponseType(typeof(LeaveRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] LeaveReviewRequest? request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var result = await _leave.ApproveAsync(_tenantContext.TenantId, id, actorAppUserId, request?.Notes);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.HrLeaveApprove)]
    [ProducesResponseType(typeof(LeaveRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] LeaveReviewRequest? request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var result = await _leave.RejectAsync(_tenantContext.TenantId, id, actorAppUserId, request?.Notes);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.HrLeaveManage)]
    [ProducesResponseType(typeof(LeaveRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var result = await _leave.CancelAsync(_tenantContext.TenantId, id, actorAppUserId, isSelfService: false, selfEmployeeId: null);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    // ── Self-service ──

    [HttpGet("me")]
    [ProducesResponseType(typeof(List<LeaveRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine([FromQuery] string? status = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var employeeId = await ResolveOwnEmployeeIdAsync();
        if (employeeId == null)
            return Forbid();

        var result = await _leave.ListAsync(_tenantContext.TenantId, employeeId, status);
        return Ok(result.Data);
    }

    [HttpPost("me")]
    [ProducesResponseType(typeof(LeaveRequestDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateMine([FromBody] CreateLeaveRequestRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var employeeId = await ResolveOwnEmployeeIdAsync();
        if (employeeId == null)
            return Forbid();

        var result = await _leave.CreateAsync(_tenantContext.TenantId, employeeId.Value, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPost("me/{id:guid}/cancel")]
    [ProducesResponseType(typeof(LeaveRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelMine(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var employeeId = await ResolveOwnEmployeeIdAsync();
        if (employeeId == null)
            return Forbid();

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var result = await _leave.CancelAsync(_tenantContext.TenantId, id, actorAppUserId, isSelfService: true, selfEmployeeId: employeeId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    private Task<Guid?> ResolveOwnEmployeeIdAsync()
    {
        var identityUserId = GetIdentityUserId();
        return identityUserId == Guid.Empty
            ? Task.FromResult<Guid?>(null)
            : _employees.ResolveEmployeeIdForCallerAsync(_tenantContext.TenantId, identityUserId);
    }

    private Task<Guid?> ResolveActingAppUserIdAsync()
    {
        var identityUserId = GetIdentityUserId();
        return identityUserId == Guid.Empty
            ? Task.FromResult<Guid?>(null)
            : _employees.ResolveAppUserIdForCallerAsync(_tenantContext.TenantId, identityUserId);
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

public class LeaveReviewRequest
{
    public string? Notes { get; set; }
}
