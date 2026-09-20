namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

[ApiController]
[Route("platform-api/customers")]
[Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformCustomerAccess")]
public class PlatformCustomersController : ControllerBase
{
    private readonly IPlatformCustomerService _customers;

    public PlatformCustomersController(IPlatformCustomerService customers)
    {
        _customers = customers;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
        => Ok(await _customers.ListCustomersAsync(status, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await _customers.GetCustomerAsync(id, ct);
        return row == null ? NotFound() : Ok(row);
    }

    [HttpGet("{id:guid}/profile")]
    public async Task<IActionResult> Profile(Guid id, CancellationToken ct)
    {
        var row = await _customers.GetProfileAsync(id, CanRevealLicenseKey(), ct);
        return row == null ? NotFound() : Ok(row);
    }

    [HttpPost]
    [Authorize(Policy = "PlatformSalesOrAbove")]
    public async Task<IActionResult> Create([FromBody] UpsertPlatformCustomerRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var created = await _customers.CreateCustomerAsync(request, actor.Value, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "PlatformSalesOrAbove")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertPlatformCustomerRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var updated = await _customers.UpdateCustomerAsync(id, request, actor.Value, ct);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Ops+ only — link/unlink Cloud TenantId. Ordinary PUT ignores TenantId.</summary>
    [HttpPut("{id:guid}/cloud-link")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    public async Task<IActionResult> SetCloudLink(Guid id, [FromBody] SetCustomerCloudLinkRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var updated = await _customers.SetCustomerCloudLinkAsync(id, request.TenantId, actor.Value, ct);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/owner-password-reset")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    public async Task<IActionResult> InitiatePasswordReset(Guid id, [FromBody] InitiateOwnerPasswordResetRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10)
            return BadRequest(new { error = "Reason must be at least 10 characters." });
        var result = await _customers.InitiateOwnerPasswordResetAsync(id, request.Reason.Trim(), actor.Value, ct);
        return result == null ? NotFound() : Ok(result);
    }

    Guid? ActorId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>Ops+/Admin may see full Local license keys embedded in the customer profile.</summary>
    bool CanRevealLicenseKey()
    {
        if (User.IsInRole(PlatformRoles.Ops) || User.IsInRole(PlatformRoles.Admin))
            return true;
        var role = User.FindFirst("role")?.Value
            ?? User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst(PlatformAuthConstants.RoleClaimType)?.Value;
        return role is PlatformRoles.Ops or PlatformRoles.Admin;
    }
}
