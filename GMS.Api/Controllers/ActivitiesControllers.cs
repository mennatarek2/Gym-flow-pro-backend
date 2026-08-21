namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Activities;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

[Route("api/activities")]
[Authorize]
public class ActivitiesController : BaseApiController
{
    private readonly IActivityService _activities;
    private readonly ITenantContext _tenantContext;

    public ActivitiesController(IActivityService activities, ITenantContext tenantContext)
    {
        _activities = activities;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(List<ActivityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _activities.ListAsync(_tenantContext.TenantId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { message = result.Error });
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(ActivityDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _activities.GetByIdAsync(_tenantContext.TenantId, id, ct);
        return result.IsSuccess ? Ok(result.Data) : NotFound(new { message = result.Error });
    }

    [HttpPost]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(ActivityDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateActivityRequest request, CancellationToken ct)
    {
        var result = await _activities.CreateAsync(_tenantContext.TenantId, request, ct);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });
        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(ActivityDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateActivityRequest request, CancellationToken ct)
    {
        var result = await _activities.UpdateAsync(_tenantContext.TenantId, id, request, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { message = result.Error });
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _activities.DeleteAsync(_tenantContext.TenantId, id, ct);
        return result.IsSuccess ? NoContent() : BadRequest(new { message = result.Error });
    }

    [HttpGet("{id:guid}/schedules")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(List<ActivityScheduleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSchedules(Guid id, CancellationToken ct)
    {
        var result = await _activities.ListSchedulesAsync(_tenantContext.TenantId, id, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { message = result.Error });
    }

    [HttpPost("{id:guid}/schedules")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(ActivityScheduleDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSchedule(Guid id, [FromBody] CreateScheduleRequest request, CancellationToken ct)
    {
        var result = await _activities.CreateScheduleAsync(_tenantContext.TenantId, id, request, ct);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });
        return Created(string.Empty, result.Data);
    }
}

[Route("api/activity-schedules")]
[Authorize]
public class ActivitySchedulesController : BaseApiController
{
    private readonly IActivityService _activities;
    private readonly ITenantContext _tenantContext;

    public ActivitySchedulesController(IActivityService activities, ITenantContext tenantContext)
    {
        _activities = activities;
        _tenantContext = tenantContext;
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _activities.DeleteScheduleAsync(_tenantContext.TenantId, id, ct);
        return result.IsSuccess ? NoContent() : BadRequest(new { message = result.Error });
    }
}

[Route("api/activity-sessions")]
[Authorize]
public class ActivitySessionsController : BaseApiController
{
    private readonly ISessionBookingService _sessions;
    private readonly ITenantContext _tenantContext;

    public ActivitySessionsController(ISessionBookingService sessions, ITenantContext tenantContext)
    {
        _sessions = sessions;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(List<SessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string date, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d))
            return BadRequest(new { message = "Invalid date / التاريخ غير صالح" });

        var result = await _sessions.GetSessionsByDateAsync(_tenantContext.TenantId, d, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { message = result.Error });
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(SessionDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _sessions.GetSessionDetailAsync(_tenantContext.TenantId, id, ct);
        return result.IsSuccess ? Ok(result.Data) : NotFound(new { message = result.Error });
    }
}

[Route("api/activity-bookings")]
[Authorize]
public class ActivityBookingsController : BaseApiController
{
    private readonly ISessionBookingService _sessions;
    private readonly ITenantContext _tenantContext;

    public ActivityBookingsController(ISessionBookingService sessions, ITenantContext tenantContext)
    {
        _sessions = sessions;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateBookingRequest request, CancellationToken ct)
    {
        var result = await _sessions.CreateBookingAsync(_tenantContext.TenantId, request, GetUserId(), ct);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });
        return Created(string.Empty, result.Data);
    }

    [HttpPut("{id:guid}/cancel")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var result = await _sessions.CancelBookingAsync(_tenantContext.TenantId, id, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { message = result.Error });
    }

    [HttpPut("{id:guid}/check-in")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckIn(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty)
            return BadRequest(new { message = "Staff user required / مطلوب مستخدم الموظف" });

        var result = await _sessions.CheckInBookingAsync(_tenantContext.TenantId, id, userId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { message = result.Error });
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return claim != null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }
}
