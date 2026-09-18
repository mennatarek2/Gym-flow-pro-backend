namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

[ApiController]
[Route("platform-api/desk-feedback")]
[Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformCustomerAccess")]
public class PlatformDeskFeedbackController : ControllerBase
{
    private readonly IDeskFeedbackService _feedback;

    public PlatformDeskFeedbackController(IDeskFeedbackService feedback)
    {
        _feedback = feedback;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? tenantId,
        [FromQuery] string? category,
        [FromQuery] string? status,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
        => Ok(await _feedback.ListAsync(customerId, tenantId, category, status, from, to, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await _feedback.GetAsync(id, ct);
        return row == null ? NotFound() : Ok(row);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "PlatformSupportOrAbove")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeskFeedbackRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var updated = await _feedback.UpdateAsync(id, request ?? new UpdateDeskFeedbackRequest(), actor.Value, ct);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    Guid? ActorId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
