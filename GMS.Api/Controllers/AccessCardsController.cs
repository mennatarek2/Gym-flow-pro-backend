namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GMS.Api.Authorization;
using GMS.Application.DTOs.AccessCards;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Physical PVC access-card inventory and lifecycle (assign / replace / lost).
/// </summary>
[Route("api/access-cards")]
[Authorize]
public class AccessCardsController : BaseApiController
{
    private readonly IAccessCardService _cards;
    private readonly ITenantSettingsService _settingsService;
    private readonly ITenantContext _tenantContext;
    private readonly GymFlowProDbContext _db;
    private readonly ILogger<AccessCardsController> _logger;

    public AccessCardsController(
        IAccessCardService cards,
        ITenantSettingsService settingsService,
        ITenantContext tenantContext,
        GymFlowProDbContext db,
        ILogger<AccessCardsController> logger)
    {
        _cards = cards;
        _settingsService = settingsService;
        _tenantContext = tenantContext;
        _db = db;
        _logger = logger;
    }

    /// <summary>GET /api/access-cards/inventory</summary>
    [HttpGet("inventory")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(AccessCardInventoryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInventory()
    {
        var result = await _cards.GetInventoryAsync(_tenantContext.TenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>
    /// Gym-branded blank-stock print sheet (CR80). Barcode = AccessCard.Code.
    /// GET /api/access-cards/print-html?batchId=…&amp;status=Available&amp;limit=100
    /// </summary>
    [HttpGet("print-html")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBatchPrintHtml(
        [FromQuery] string? batchId,
        [FromQuery] string? status = AccessCardStatuses.Available,
        [FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        var tenantId = _tenantContext.TenantId;

        var query = _db.AccessCards.AsNoTracking()
            .Where(c => c.TenantId == tenantId && !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(batchId))
            query = query.Where(c => c.BatchId == batchId.Trim());

        if (!string.IsNullOrWhiteSpace(status) && AccessCardStatuses.IsKnown(status))
            query = query.Where(c => c.Status == status.Trim());

        var codes = await query
            .OrderBy(c => c.Code)
            .Take(limit)
            .Select(c => c.Code)
            .ToListAsync();

        if (codes.Count == 0)
            return BadRequest("No cards match this filter / لا توجد كارنيهات مطابقة");

        var branding = await _settingsService.GetBrandingAsync(tenantId);
        var gymName = branding.IsSuccess ? branding.Data!.GymName : string.Empty;
        var gymNameAr = branding.IsSuccess ? branding.Data!.GymNameAr : string.Empty;
        var primary = branding.IsSuccess ? branding.Data!.CardPrimaryColor : null;
        var showLogo = branding.IsSuccess && branding.Data!.ShowGymLogoOnCard;
        var logo = showLogo && branding.IsSuccess ? branding.Data!.LogoUrl : null;
        if (!string.IsNullOrWhiteSpace(logo) && logo.StartsWith('/'))
            logo = $"{Request.Scheme}://{Request.Host}{logo}";

        var html = AccessCardHtmlBuilder.BuildBlankStockBatch(
            codes, gymName, gymNameAr, logo, primary, showLogo);

        return Content(html, "text/html");
    }

    /// <summary>GET /api/access-cards?status=&amp;search=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _cards.ListAsync(_tenantContext.TenantId, status, search, page, pageSize);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>GET /api/access-cards/{id}</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(AccessCardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _cards.GetByIdAsync(_tenantContext.TenantId, id);
        return result.IsSuccess ? Ok(result.Data) : NotFound(result.Error);
    }

    /// <summary>GET /api/access-cards/by-member/{memberId}</summary>
    [HttpGet("by-member/{memberId:guid}")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(AccessCardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetByMember(Guid memberId)
    {
        var result = await _cards.GetAssignedForMemberAsync(_tenantContext.TenantId, memberId);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        if (result.Data == null)
            return NoContent();
        return Ok(result.Data);
    }

    /// <summary>POST /api/access-cards/bulk</summary>
    [HttpPost("bulk")]
    [HasPermission(Permissions.MembersCreate)]
    [ProducesResponseType(typeof(BulkCreateAccessCardsResult), StatusCodes.Status201Created)]
    public async Task<IActionResult> BulkCreate([FromBody] BulkCreateAccessCardsRequest request)
    {
        var result = await _cards.BulkCreateAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("Bulk create cards failed: {Error}", result.Error);
            return BadRequest(result.Error);
        }
        return StatusCode(StatusCodes.Status201Created, result.Data);
    }

    /// <summary>POST /api/access-cards/assign</summary>
    [HttpPost("assign")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(AccessCardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Assign([FromBody] AssignAccessCardRequest request)
    {
        var result = await _cards.AssignAsync(_tenantContext.TenantId, request);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>POST /api/access-cards/replace</summary>
    [HttpPost("replace")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(AccessCardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Replace([FromBody] ReplaceAccessCardRequest request)
    {
        var result = await _cards.ReplaceAsync(_tenantContext.TenantId, request);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>POST /api/access-cards/{id}/lost</summary>
    [HttpPost("{id:guid}/lost")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(AccessCardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkLost(Guid id, [FromBody] MarkAccessCardRequest? request)
    {
        var result = await _cards.MarkLostAsync(_tenantContext.TenantId, id, request ?? new MarkAccessCardRequest());
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>POST /api/access-cards/{id}/damaged</summary>
    [HttpPost("{id:guid}/damaged")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(AccessCardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkDamaged(Guid id, [FromBody] MarkAccessCardRequest? request)
    {
        var result = await _cards.MarkDamagedAsync(_tenantContext.TenantId, id, request ?? new MarkAccessCardRequest());
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>POST /api/access-cards/{id}/block</summary>
    [HttpPost("{id:guid}/block")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(AccessCardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Block(Guid id, [FromBody] MarkAccessCardRequest? request)
    {
        var result = await _cards.BlockAsync(_tenantContext.TenantId, id, request ?? new MarkAccessCardRequest());
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }

    /// <summary>POST /api/access-cards/{id}/unassign</summary>
    [HttpPost("{id:guid}/unassign")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(AccessCardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Unassign(Guid id, [FromBody] UnassignAccessCardRequest? request)
    {
        var result = await _cards.UnassignAsync(_tenantContext.TenantId, id, request ?? new UnassignAccessCardRequest());
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error);
    }
}
