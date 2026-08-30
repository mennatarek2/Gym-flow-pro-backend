namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>HR Foundation: positions.</summary>
[Route("api/hr/positions")]
[Authorize]
[FeatureFlag("hr")]
public class HrPositionsController : BaseApiController
{
    private readonly IPositionService _positions;
    private readonly ITenantContext _tenantContext;

    public HrPositionsController(IPositionService positions, ITenantContext tenantContext)
    {
        _positions = positions;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.HrView)]
    [ProducesResponseType(typeof(List<PositionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, [FromQuery] Guid? departmentId = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _positions.ListAsync(_tenantContext.TenantId, includeInactive, departmentId);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.HrView)]
    [ProducesResponseType(typeof(PositionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _positions.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(PositionDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreatePositionRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _positions.CreateAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(PositionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePositionRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _positions.UpdateAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }
}
