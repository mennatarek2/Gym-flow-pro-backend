namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Base controller for all API endpoints with common functionality.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    protected IActionResult NotFound(string message)
    {
        return NotFound(new { message });
    }

    protected IActionResult BadRequest(string message)
    {
        return BadRequest(new { message });
    }

    protected IActionResult InternalServerError(string message)
    {
        return StatusCode(StatusCodes.Status500InternalServerError, new { message });
    }
}
