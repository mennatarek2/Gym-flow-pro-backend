namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Health check controller for monitoring application status.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : BaseApiController
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "Healthy", timestamp = DateTime.UtcNow });
    }
}
