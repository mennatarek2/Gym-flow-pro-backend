namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

[ApiController]
[Route("platform-api")]
[Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformCustomerAccess")]
public class LocalSalesContractsController : ControllerBase
{
    private readonly ILocalSalesContractService _docs;

    public LocalSalesContractsController(ILocalSalesContractService docs)
    {
        _docs = docs;
    }

    [HttpGet("local-sales-contract-terms")]
    public async Task<IActionResult> GetTerms(CancellationToken ct)
        => Ok(await _docs.GetTermsAsync(ct));

    [HttpPut("local-sales-contract-terms")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> UpdateTerms([FromBody] UpsertLocalSalesContractTermsRequest request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            return Ok(await _docs.UpdateTermsAsync(request, actor.Value, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("local-sales-contracts")]
    public async Task<IActionResult> List([FromQuery] Guid? customerId, [FromQuery] Guid? contractId, CancellationToken ct)
        => Ok(await _docs.ListIssuedAsync(customerId, contractId, ct));

    [HttpGet("contracts/{contractId:guid}/sales-contract")]
    public async Task<IActionResult> GetIssued(Guid contractId, CancellationToken ct)
    {
        var row = await _docs.GetIssuedAsync(contractId, ct);
        return row == null ? NotFound() : Ok(row);
    }

    [HttpGet("contracts/{contractId:guid}/sales-contract/preview")]
    public async Task<IActionResult> Preview(Guid contractId, [FromQuery] string? language, CancellationToken ct)
    {
        try
        {
            return Ok(await _docs.PreviewAsync(contractId, language, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("contracts/{contractId:guid}/sales-contract/html")]
    public async Task<IActionResult> IssuedHtml(Guid contractId, CancellationToken ct)
    {
        var row = await _docs.GetIssuedHtmlAsync(contractId, ct);
        return row == null ? NotFound() : Ok(row);
    }

    [HttpPost("contracts/{contractId:guid}/sales-contract")]
    [Authorize(Policy = "PlatformSalesOrAbove")]
    public async Task<IActionResult> Issue(Guid contractId, [FromBody] IssueLocalSalesContractRequest? request, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            var issued = await _docs.IssueAsync(contractId, request?.Language, actor.Value, ct);
            return CreatedAtAction(nameof(GetIssued), new { contractId }, issued);
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

    [HttpPost("contracts/{contractId:guid}/sales-contract/reprint")]
    [Authorize(Policy = "PlatformCustomerAccess")]
    public async Task<IActionResult> Reprint(Guid contractId, CancellationToken ct)
    {
        var actor = ActorId();
        if (actor == null) return Unauthorized();
        try
        {
            return Ok(await _docs.ReprintAsync(contractId, actor.Value, ct));
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
