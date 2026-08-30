namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>HR Phase 3: reusable shift templates (Morning/Evening/Night, etc.).</summary>
[Route("api/hr/employee-shifts")]
[Authorize]
[FeatureFlag("hr")]
public class HrEmployeeShiftsController : BaseApiController
{
    private readonly IEmployeeShiftService _shifts;
    private readonly ITenantContext _tenantContext;

    public HrEmployeeShiftsController(IEmployeeShiftService shifts, ITenantContext tenantContext)
    {
        _shifts = shifts;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.HrView)]
    [ProducesResponseType(typeof(List<EmployeeShiftDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _shifts.ListAsync(_tenantContext.TenantId, includeInactive);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.HrView)]
    [ProducesResponseType(typeof(EmployeeShiftDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _shifts.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.HrShiftsManage)]
    [ProducesResponseType(typeof(EmployeeShiftDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeShiftRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _shifts.CreateAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.HrShiftsManage)]
    [ProducesResponseType(typeof(EmployeeShiftDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEmployeeShiftRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _shifts.UpdateAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }
}
