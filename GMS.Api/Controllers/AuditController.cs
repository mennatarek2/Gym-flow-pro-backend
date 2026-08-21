namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.Common;
using GMS.Application.DTOs.Audit;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Read-only access to the audit trail for the current tenant.
/// </summary>
[Route("api/audit")]
[Authorize]
public class AuditController : BaseApiController
{
    private readonly IAuditService _auditService;
    private readonly ITenantContext _tenantContext;

    public AuditController(IAuditService auditService, ITenantContext tenantContext)
    {
        _auditService = auditService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// GET /api/audit?entityType=&amp;entityId=&amp;from=&amp;to=&amp;action=&amp;page=&amp;pageSize=
    /// All filters optional.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.SettingsManage)]
    [ProducesResponseType(typeof(PagedResult<AuditEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAuditEvents([FromQuery] AuditEventQueryRequest query)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _auditService.GetAuditEventsAsync(tenantId, query);

        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }
}
