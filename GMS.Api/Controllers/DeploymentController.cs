namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Core.Configuration;

/// <summary>
/// Read-only edition info so the frontend can decide, before login, whether to show the Local
/// first-run setup wizard or the normal SaaS login flow. Deliberately anonymous and minimal —
/// no tenant/user data, just which edition this instance is running as.
/// </summary>
[Route("api/deployment")]
[AllowAnonymous]
public class DeploymentController : BaseApiController
{
    private readonly DeploymentEdition _edition;

    public DeploymentController(DeploymentEdition edition)
    {
        _edition = edition;
    }

    /// <summary>GET /api/deployment/info</summary>
    [HttpGet("info")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetInfo()
    {
        return Ok(new { edition = _edition.ToString() });
    }
}
