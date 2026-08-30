namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>HR Foundation: departments.</summary>
[Route("api/hr/departments")]
[Authorize]
[FeatureFlag("hr")]
public class HrDepartmentsController : BaseApiController
{
    private readonly IDepartmentService _departments;
    private readonly ITenantContext _tenantContext;

    public HrDepartmentsController(IDepartmentService departments, ITenantContext tenantContext)
    {
        _departments = departments;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.HrView)]
    [ProducesResponseType(typeof(List<DepartmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _departments.ListAsync(_tenantContext.TenantId, includeInactive);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.HrView)]
    [ProducesResponseType(typeof(DepartmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _departments.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(DepartmentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _departments.CreateAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(DepartmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _departments.UpdateAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }
}
