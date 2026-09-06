namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using GMS.Application.Common;

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

    /// <summary>Additive bilingual error body: code + error (slash) + message + messageAr.</summary>
    protected IActionResult BadRequest(AppError error) =>
        BadRequest(error.ToAnonymousBody());

    protected IActionResult NotFound(AppError error) =>
        NotFound(error.ToAnonymousBody());

    protected IActionResult FailureResult(Result result, int statusCode = StatusCodes.Status400BadRequest)
    {
        if (result.IsSuccess) return Ok(new { message = result.Message });
        return StatusCode(statusCode, new { error = result.Error, message = result.Message ?? result.Error });
    }

    protected IActionResult InternalServerError(string message)
    {
        return StatusCode(StatusCodes.Status500InternalServerError, new { message });
    }
}
