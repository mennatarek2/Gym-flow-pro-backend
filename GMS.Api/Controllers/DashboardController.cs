namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Dashboard;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

[Route("api/dashboard")]
[Authorize(Roles = "Owner,Manager,Receptionist,Trainer")]
public sealed class DashboardController : BaseApiController
{
    private readonly IDashboardService _dashboard;
    private readonly ITenantContext _tenantContext;

    public DashboardController(IDashboardService dashboard, ITenantContext tenantContext)
    {
        _dashboard = dashboard;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Returns the real, role-filtered dashboard payload. Financial fields are
    /// omitted server-side for roles without reports.financial.view.
    /// </summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(DashboardOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] DashboardQuery query,
        CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        if (query.From.HasValue != query.To.HasValue)
            return BadRequest(new { error = "from and to must be supplied together." });
        if (query.From.HasValue && query.From > query.To)
            return BadRequest(new { error = "from must be before to." });

        var result = await _dashboard.GetOverviewAsync(
            _tenantContext.TenantId,
            query,
            BuildAccessContext(),
            ct);

        return result.IsSuccess
            ? Ok(result.Data)
            : BadRequest(new { error = result.Error, message = result.Message });
    }

    private DashboardAccessContext BuildAccessContext()
    {
        var role = User.FindFirstValue(ClaimTypes.Role)
                   ?? User.FindFirstValue("role")
                   ?? string.Empty;
        var permissions = User.FindAll(Permissions.ClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        Guid? userId = null;
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? User.FindFirstValue("sub");
        if (Guid.TryParse(rawUserId, out var parsed))
            userId = parsed;

        return new DashboardAccessContext
        {
            Role = role,
            UserId = userId,
            Permissions = permissions
        };
    }
}
