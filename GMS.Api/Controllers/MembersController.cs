namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Members;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Full CRUD controller for gym members.
/// All queries auto-filtered by tenant_id via EF Core Global Filter.
/// </summary>
[Route("api/members")]
[Authorize]
public class MembersController : BaseApiController
{
    private readonly IMemberService _memberService;
    private readonly IRefundService _refundService;
    private readonly IMemberAppActivationService _memberAppActivation;
    private readonly ITenantSettingsService _settingsService;
    private readonly ITenantContext _tenantContext;
    private readonly GymFlowProDbContext _dbContext;
    private readonly ILogger<MembersController> _logger;

    public MembersController(
        IMemberService memberService,
        IRefundService refundService,
        IMemberAppActivationService memberAppActivation,
        ITenantSettingsService settingsService,
        ITenantContext tenantContext,
        GymFlowProDbContext dbContext,
        ILogger<MembersController> logger)
    {
        _memberService = memberService;
        _refundService = refundService;
        _memberAppActivation = memberAppActivation;
        _settingsService = settingsService;
        _tenantContext = tenantContext;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// List/search members with pagination and status filter.
    /// GET /api/members?search=ahmed&amp;status=active&amp;page=1&amp;pageSize=20
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMembers(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _memberService.GetMembersAsync(tenantId, search, status, page, pageSize);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error!);
    }

    /// <summary>
    /// Get member full details with current membership and recent attendance.
    /// GET /api/members/{id}
    /// </summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(MemberDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMember(Guid id)
    {
        var result = await _memberService.GetMemberByIdAsync(id);
        return result.IsSuccess ? Ok(result.Data) : NotFound(result.Error!);
    }

    /// <summary>
    /// Create a new member with auto-generated MemberNumber.
    /// POST /api/members
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.MembersCreate)]
    [ProducesResponseType(typeof(MemberDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateMember([FromBody] CreateMemberRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _memberService.CreateMemberAsync(tenantId, request);

        if (!result.IsSuccess)
            return BadRequest(result.Error!);

        if (result.Message?.StartsWith("PLAN_SOFT_CAP:", StringComparison.Ordinal) == true)
            Response.Headers["X-Plan-Soft-Cap"] = result.Message["PLAN_SOFT_CAP:".Length..];

        return CreatedAtAction(
            nameof(GetMember),
            new { id = result.Data!.Id },
            result.Data);
    }

    /// <summary>
    /// Update member details (partial update — only provided fields).
    /// PUT /api/members/{id}
    /// </summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(MemberDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMember(Guid id, [FromBody] UpdateMemberRequest request)
    {
        var result = await _memberService.UpdateMemberAsync(id, request);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error!);
    }

    /// <summary>
    /// Deactivate a member (soft — IsActive = false). Does not expire memberships.
    /// Owner only.
    /// DELETE /api/members/{id}
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "OwnerOnly")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateMember(Guid id)
    {
        var result = await _memberService.DeactivateMemberAsync(id);
        return result.IsSuccess ? Ok(new { message = result.Data }) : NotFound(result.Error!);
    }

    /// <summary>
    /// Reactivate a deactivated member account (IsActive = true).
    /// Does not assign a membership — use assign/renew for plans.
    /// Owner only.
    /// POST /api/members/{id}/reactivate
    /// </summary>
    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = "OwnerOnly")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateMember(Guid id)
    {
        var result = await _memberService.ReactivateMemberAsync(id);
        return result.IsSuccess ? Ok(new { message = result.Data }) : NotFound(result.Error!);
    }

    /// <summary>
    /// Printable access card HTML (CODE128 of MemberNumber). MAC-P0 Phase 1.
    /// GET /api/members/{id}/access-card-html
    /// </summary>
    [HttpGet("{id:guid}/access-card-html")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccessCardHtml(Guid id)
    {
        var result = await _memberService.GetMemberByIdAsync(id);
        if (!result.IsSuccess || result.Data == null)
            return NotFound(result.Error ?? "Member not found");

        var tenant = await _dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        var branding = await _settingsService.GetBrandingAsync(_tenantContext.TenantId);
        var primary = branding.IsSuccess ? branding.Data!.CardPrimaryColor : null;
        var logo = branding.IsSuccess && branding.Data!.ShowGymLogoOnCard
            ? branding.Data.LogoUrl
            : null;
        // Absolute logo for print iframe on another origin
        if (!string.IsNullOrWhiteSpace(logo) && logo.StartsWith('/'))
            logo = $"{Request.Scheme}://{Request.Host}{logo}";

        var html = AccessCardHtmlBuilder.Build(
            result.Data,
            tenant?.Name ?? branding.Data?.GymName ?? string.Empty,
            tenant?.NameAr ?? branding.Data?.GymNameAr ?? string.Empty,
            logo,
            primary,
            showGymLogo: branding.IsSuccess ? branding.Data!.ShowGymLogoOnCard : true);

        return Content(html, "text/html");
    }

    /// <summary>
    /// Get member attendance history (paginated).
    /// GET /api/members/{id}/attendance?page=1&amp;pageSize=20
    /// </summary>
    [HttpGet("{id:guid}/attendance")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMemberAttendance(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _memberService.GetMemberAttendanceAsync(id, page, pageSize);
        return result.IsSuccess ? Ok(result.Data) : NotFound(result.Error!);
    }

    /// <summary>
    /// Get member's current active/frozen membership.
    /// GET /api/members/{id}/membership
    /// </summary>
    [HttpGet("{id:guid}/membership")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(MembershipSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentMembership(Guid id)
    {
        var result = await _memberService.GetCurrentMembershipAsync(id);
        return result.IsSuccess ? Ok(result.Data) : NotFound(result.Error!);
    }

    /// <summary>
    /// Freeze a member's active membership.
    /// End date is extended by the freeze duration.
    /// POST /api/members/{id}/freeze
    /// </summary>
    [HttpPost("{id:guid}/freeze")]
    [HasPermission(Permissions.MembershipsFreeze)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FreezeMembership(Guid id, [FromBody] FreezeMembershipRequest request)
    {
        var result = await _memberService.FreezeMembershipAsync(id, request.FrozenUntil, request.Reason);
        return result.IsSuccess ? Ok(new { message = result.Data }) : BadRequest(result.Error!);
    }

    /// <summary>
    /// Unfreeze a member's frozen membership back to active.
    /// POST /api/members/{id}/unfreeze
    /// </summary>
    [HttpPost("{id:guid}/unfreeze")]
    [HasPermission(Permissions.MembershipsFreeze)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UnfreezeMembership(Guid id)
    {
        var result = await _memberService.UnfreezeMembershipAsync(id);
        return result.IsSuccess ? Ok(new { message = result.Data }) : BadRequest(result.Error!);
    }

    /// <summary>
    /// Get a member's account-credit balance and ledger entries.
    /// GET /api/members/{id}/credits
    /// </summary>
    [HttpGet("{id:guid}/credits")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberCredits(Guid id)
    {
        var result = await _refundService.GetMemberCreditSummaryAsync(id, _tenantContext.TenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.Error!);
    }

    /// <summary>
    /// Generate a one-time Member App activation code (plaintext returned once).
    /// Invalidates any previous unused code for this member.
    /// POST /api/members/{id}/app-activation-code
    /// </summary>
    [HttpPost("{id:guid}/app-activation-code")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(MemberAppActivationCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateAppActivationCode(Guid id)
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        Guid? createdBy = Guid.TryParse(sub, out var g) ? g : null;

        var result = await _memberAppActivation.GenerateAsync(id, createdBy);
        if (!result.IsSuccess)
        {
            if (result.Error != null && result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { error = result.Error });
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Data);
    }
}
