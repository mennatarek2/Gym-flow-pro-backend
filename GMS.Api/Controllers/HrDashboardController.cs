namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>HR Phase 6: real-data dashboard metrics. Payroll figures are included only when the
/// caller also holds hr.payroll.view — payroll stays private even on an aggregated dashboard.</summary>
[Route("api/hr/dashboard")]
[Authorize]
[FeatureFlag("hr")]
public class HrDashboardController : BaseApiController
{
    private readonly IHrDashboardService _dashboard;
    private readonly ITenantContext _tenantContext;

    public HrDashboardController(IHrDashboardService dashboard, ITenantContext tenantContext)
    {
        _dashboard = dashboard;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.HrView)]
    [ProducesResponseType(typeof(HrDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var includePayroll = User.HasClaim(Permissions.ClaimType, Permissions.HrPayrollView);
        var result = await _dashboard.GetAsync(_tenantContext.TenantId, includePayroll);
        return Ok(result.Data);
    }
}
