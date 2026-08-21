namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Attendance;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>Member App live occupancy. Same numbers as staff GET /api/attendance/occupancy.</summary>
[Route("api/member/occupancy")]
[Authorize(Policy = "AuthenticatedMember")]
public class MemberOccupancyController : BaseApiController
{
    private readonly IGymOccupancyService _occupancy;
    private readonly ITenantContext _tenantContext;

    public MemberOccupancyController(IGymOccupancyService occupancy, ITenantContext tenantContext)
    {
        _occupancy = occupancy;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(GymOccupancyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _occupancy.GetOccupancyAsync(_tenantContext.TenantId, ct);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }
}
