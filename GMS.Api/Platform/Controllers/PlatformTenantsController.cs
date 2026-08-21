namespace GMS.Api.Platform.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Provisioning;
using GMS.Application.Interfaces;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>Platform Console tenant read + admin actions (CP6).</summary>
[ApiController]
[Route("platform-api/tenants")]
[Authorize(
    AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme,
    Policy = "PlatformSupportOrAbove")]
public class PlatformTenantsController : ControllerBase
{
    private readonly IPlatformTenantReadService _tenants;
    private readonly IPlatformTenantAdminService _admin;
    private readonly IPlatformImpersonationService _impersonation;
    private readonly ISubscriptionService _subscriptions;

    public PlatformTenantsController(
        IPlatformTenantReadService tenants,
        IPlatformTenantAdminService admin,
        IPlatformImpersonationService impersonation,
        ISubscriptionService subscriptions)
    {
        _tenants = tenants;
        _admin = admin;
        _impersonation = impersonation;
        _subscriptions = subscriptions;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PlatformPagedResult<PlatformTenantListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? tier,
        [FromQuery] string? riskBand,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _tenants.ListAsync(status, tier, riskBand, search, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{tenantId:guid}")]
    [ProducesResponseType(typeof(PlatformTenantDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        var detail = await _tenants.GetDetailAsync(tenantId, ct);
        if (detail == null)
            return NotFound(new { errorCode = "TENANT_NOT_FOUND", message = "Tenant not found." });
        return Ok(detail);
    }

    [HttpGet("{tenantId:guid}/subscription/changes")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionChangeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubscriptionChanges(Guid tenantId, CancellationToken ct)
    {
        var changes = await _tenants.GetSubscriptionChangesAsync(tenantId, ct);
        return Ok(changes);
    }

    [HttpGet("{tenantId:guid}/invoices")]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformInvoiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvoices(Guid tenantId, CancellationToken ct)
    {
        var invoices = await _tenants.GetInvoicesAsync(tenantId, ct);
        return Ok(invoices);
    }

    [HttpPost("{tenantId:guid}/coupon")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ApplyCoupon(Guid tenantId, [FromBody] CreateCouponRequest request, CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _admin.ApplyCouponAsync(tenantId, actor.Value, request, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("{tenantId:guid}/extend-trial")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(typeof(SubscriptionStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExtendTrial(Guid tenantId, [FromBody] ExtendTrialRequest request, CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var (result, subscription) = await _admin.ExtendTrialAsync(tenantId, actor.Value, request, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(subscription);
    }

    /// <summary>
    /// Ops repair for orphan gyms: create a trial when no live <c>platform.subscriptions</c> row exists.
    /// Does not backfill from list reads. Prefer <see cref="Provision"/> for new gyms.
    /// </summary>
    [HttpPost("{tenantId:guid}/start-trial")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(typeof(SubscriptionStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartTrial(
        Guid tenantId,
        [FromBody] StartTrialRequest? request,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var detail = await _tenants.GetDetailAsync(tenantId, ct);
        if (detail == null)
            return NotFound(new { errorCode = "TENANT_NOT_FOUND", message = "Tenant not found." });

        var tier = string.IsNullOrWhiteSpace(request?.Tier) ? PlanTiers.Growth : request.Tier!.Trim();
        var result = await _subscriptions.StartTrialAsync(
            tenantId,
            tier,
            SubscriptionInitiators.PlatformAdmin,
            actor.Value,
            ct);

        if (!result.Success)
        {
            if (string.Equals(result.ErrorCode, "LIVE_SUBSCRIPTION_EXISTS", StringComparison.Ordinal))
                return Conflict(result);
            return BadRequest(result);
        }

        return Ok(result.Subscription);
    }

    [HttpPost("{tenantId:guid}/force-suspend")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(typeof(SubscriptionStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForceSuspend(Guid tenantId, [FromBody] ForceSuspendRequest request, CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var (result, subscription) = await _admin.ForceSuspendAsync(tenantId, actor.Value, request, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(subscription);
    }

    [HttpPost("{tenantId:guid}/force-reactivate")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(typeof(SubscriptionStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForceReactivate(Guid tenantId, [FromBody] ForceReactivateRequest request, CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var (result, subscription) = await _admin.ForceReactivateAsync(tenantId, actor.Value, request, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(subscription);
    }

    [HttpGet("{tenantId:guid}/feature-overrides")]
    [ProducesResponseType(typeof(IReadOnlyList<FeatureOverrideDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFeatureOverrides(Guid tenantId, CancellationToken ct)
    {
        var items = await _admin.ListFeatureOverridesAsync(tenantId, ct);
        return Ok(items);
    }

    [HttpPost("{tenantId:guid}/feature-overrides")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(typeof(FeatureOverrideDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertFeatureOverride(
        Guid tenantId,
        [FromBody] UpsertFeatureOverrideRequest request,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var (result, row) = await _admin.UpsertFeatureOverrideAsync(tenantId, actor.Value, request, ClientIp(), ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(row);
    }

    [HttpDelete("{tenantId:guid}/feature-overrides/{overrideId:guid}")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFeatureOverride(Guid tenantId, Guid overrideId, CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        var result = await _admin.DeleteFeatureOverrideAsync(tenantId, overrideId, actor.Value, ClientIp(), ct);
        if (!result.Success)
            return result.ErrorCode == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
        return Ok(result);
    }

    [HttpPost("{tenantId:guid}/impersonate")]
    [ProducesResponseType(typeof(ImpersonationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Impersonate(
        Guid tenantId,
        [FromBody] ImpersonateRequest request,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request?.Reason) || request.Reason.Trim().Length < 10)
            return BadRequest(PlatformActionResult.Fail(
                "REASON_REQUIRED",
                "reason is required (min 10 characters) for the platform audit log."));

        var result = await _impersonation.CreateAsync(
            tenantId, actor.Value, request.Reason.Trim(), ClientIp(), ct);
        if (!result.Success)
        {
            if (result.ErrorCode == "TENANT_NOT_FOUND" || result.ErrorCode == "OWNER_NOT_FOUND")
                return NotFound(result);
            return BadRequest(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Provision a new gym: Tenant + Owner + default plans/settings + StartTrial.
    /// Ops+ only. Does not use Development DataSeeder.
    /// </summary>
    [HttpPost("provision")]
    [Authorize(Policy = "PlatformOpsOrAbove")]
    [ProducesResponseType(typeof(ProvisionTenantResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Provision(
        [FromBody] ProvisionTenantRequest request,
        [FromServices] ITenantProvisioningService provisioning,
        CancellationToken ct)
    {
        var actor = RequireActorId();
        if (actor == null)
            return Unauthorized(new { errorCode = "UNAUTHORIZED", message = "Missing platform actor id." });

        var result = await provisioning.ProvisionAsync(request, actor.Value, ClientIp(), ct);
        if (!result.IsSuccess)
            return BadRequest(new { errorCode = "PROVISION_FAILED", message = result.Error, detail = result.Message });

        return CreatedAtAction(nameof(Get), new { tenantId = result.Data!.TenantId }, result.Data);
    }

    private Guid? RequireActorId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
