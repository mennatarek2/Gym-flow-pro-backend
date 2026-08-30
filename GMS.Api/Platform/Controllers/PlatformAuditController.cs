namespace GMS.Api.Platform.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>Global (cross-tenant) platform audit feed — reads the same platform_audit_log table
/// as the per-tenant RecentAudit panel, just without a mandatory tenant filter.</summary>
[ApiController]
[Route("platform-api/audit")]
[Authorize(
    AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme,
    Policy = "PlatformSupportOrAbove")]
public class PlatformAuditController : ControllerBase
{
    private readonly IPlatformAuditService _audit;

    public PlatformAuditController(IPlatformAuditService audit)
    {
        _audit = audit;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PlatformPagedResult<PlatformAuditLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? action,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _audit.ListAsync(tenantId, action, from, to, page, pageSize, ct);
        return Ok(result);
    }
}
