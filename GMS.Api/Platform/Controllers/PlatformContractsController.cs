namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

[ApiController]
[Route("platform-api/contracts")]
[Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformCustomerAccess")]
public class PlatformContractsController : ControllerBase
{
    private readonly IPlatformCustomerService _commerce;

    public PlatformContractsController(IPlatformCustomerService commerce)
    {
        _commerce = commerce;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? customerId, CancellationToken ct)
        => Ok(await _commerce.ListContractsAsync(customerId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await _commerce.GetContractAsync(id, ct);
        return row == null ? NotFound() : Ok(row);
    }

    [HttpPost]
    [Authorize(Policy = "PlatformSalesOrAbove")]
    public async Task<IActionResult> Create([FromBody] CreatePlatformContractRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var created = await _commerce.CreateContractAsync(request, actor.Value, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/status")]
    [Authorize(Policy = "PlatformSalesOrAbove")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeContractStatusRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var updated = await _commerce.ChangeContractStatusAsync(id, request.Status, actor.Value, ct);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
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
[Route("platform-api/customer-payments")]
[Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformCustomerAccess")]
public class PlatformCustomerPaymentsController : ControllerBase
{
    private readonly IPlatformCustomerService _commerce;

    public PlatformCustomerPaymentsController(IPlatformCustomerService commerce)
    {
        _commerce = commerce;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? customerId, [FromQuery] Guid? contractId, CancellationToken ct)
        => Ok(await _commerce.ListPaymentsAsync(customerId, contractId, ct));

    [HttpPost]
    [Authorize(Policy = "PlatformSalesOrAbove")]
    public async Task<IActionResult> Record([FromBody] RecordCustomerPaymentRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var created = await _commerce.RecordPaymentAsync(request, actor.Value, ct);
            return Ok(created);
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
