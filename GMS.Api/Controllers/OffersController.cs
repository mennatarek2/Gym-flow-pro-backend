namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Offers;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>Staff Offers CRUD. Promo-code redemption syncs to promo_codes for POS.</summary>
[Route("api/offers")]
[Authorize]
public class OffersController : BaseApiController
{
    private readonly IOfferService _offers;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<OffersController> _logger;

    public OffersController(IOfferService offers, ITenantContext tenantContext, ILogger<OffersController> logger)
    {
        _offers = offers;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(typeof(List<OfferDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _offers.ListStaffAsync(_tenantContext.TenantId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(typeof(OfferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _offers.GetStaffByIdAsync(_tenantContext.TenantId, id, ct);
        return result.IsSuccess ? Ok(result.Data) : NotFound(new { error = result.Error });
    }

    [HttpPost]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(OfferDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] UpsertOfferRequest request, CancellationToken ct)
    {
        var result = await _offers.CreateAsync(_tenantContext.TenantId, request, ct);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("Failed to create offer: {Error}", result.Error);
            return BadRequest(new { error = result.Error });
        }

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(OfferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertOfferRequest request, CancellationToken ct)
    {
        var result = await _offers.UpdateAsync(_tenantContext.TenantId, id, request, ct);
        if (!result.IsSuccess)
        {
            if (result.Error != null && result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { error = result.Error });
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Data);
    }

    /// <summary>Sets end date to yesterday (Cairo) so the offer is expired immediately, and deactivates a linked promo code.</summary>
    [HttpPost("{id:guid}/end")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(OfferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> End(Guid id, CancellationToken ct)
    {
        var result = await _offers.EndAsync(_tenantContext.TenantId, id, ct);
        return result.IsSuccess ? Ok(result.Data) : NotFound(new { error = result.Error });
    }
}
