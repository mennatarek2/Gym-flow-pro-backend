namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

[ApiController]
[Route("platform-api/catalog-products")]
[Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformCustomerAccess")]
public class PlatformCatalogProductsController : ControllerBase
{
    private readonly IPlatformCustomerService _commerce;

    public PlatformCatalogProductsController(IPlatformCustomerService commerce)
    {
        _commerce = commerce;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _commerce.ListProductsAsync(includeInactive, ct));

    [HttpPost]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    public async Task<IActionResult> Create([FromBody] UpsertCatalogProductRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var created = await _commerce.CreateProductAsync(request, actor.Value, ct);
            return Ok(created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertCatalogProductRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var updated = await _commerce.UpdateProductAsync(id, request, actor.Value, ct);
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

[ApiController]
[Route("platform-api/support-tickets")]
[Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformCustomerAccess")]
public class PlatformSupportTicketsController : ControllerBase
{
    private readonly IPlatformCustomerService _commerce;

    public PlatformSupportTicketsController(IPlatformCustomerService commerce)
    {
        _commerce = commerce;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? customerId, [FromQuery] string? status, CancellationToken ct)
        => Ok(await _commerce.ListTicketsAsync(customerId, status, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await _commerce.GetTicketAsync(id, ct);
        return row == null ? NotFound() : Ok(row);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupportTicketRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var created = await _commerce.CreateTicketAsync(request, actor.Value, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "PlatformSupportOrAbove")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSupportTicketRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var updated = await _commerce.UpdateTicketAsync(id, request, actor.Value, ct);
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
