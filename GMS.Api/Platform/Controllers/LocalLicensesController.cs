namespace GMS.Api.Platform.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>
/// Platform Console management of HyMotion Local Lifetime licenses.
///
/// Authorization shape is the crux of rule 12 ("Sales Reps must never have the technical ability
/// to create arbitrary production licenses"): GET is PlatformCustomerAccess so Support can see
/// Local license/installation state on a gym, and Sales can report. Every mutating action
/// (Issue/Suspend/Revoke/Reactivate/Transfer) is individually re-gated to PlatformOpsOrAbove or
/// PlatformAdminOnly - a Sales/Support user hitting those actions directly (bypassing any
/// frontend hiding) gets a real 403 from ASP.NET's authorization middleware, not a UI-only
/// restriction.
/// </summary>
[ApiController]
[Route("platform-api/local-licenses")]
[Authorize(
    AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme,
    Policy = "PlatformCustomerAccess")]
public class LocalLicensesController : ControllerBase
{
    private readonly ILocalLicenseService _licenses;
    private readonly IPlatformAuditService _audit;

    public LocalLicensesController(ILocalLicenseService licenses, IPlatformAuditService audit)
    {
        _licenses = licenses;
        _audit = audit;
    }

    private Guid CurrentPlatformAdminUserId()
    {
        var sub = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    /// <summary>GET /platform-api/local-licenses — read access for Sales/Support/Ops/Admin.
    /// Sales/Support receive a masked LicenseKey; Ops+/Admin get the full key for support workflows.
    /// Sales view is not yet Deal-owned (full list); narrowing is deferred, not silently done here.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        return Ok(await _licenses.ListAsync(CanRevealLicenseKey(), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _licenses.GetDetailAsync(id, CanRevealLicenseKey(), cancellationToken);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    /// <summary>POST /platform-api/local-licenses — Ops/Admin only. This is the ONLY way a
    /// production license is ever created; there is no "master license" and no Sales-accessible
    /// path to this action (rule 22).</summary>
    [HttpPost]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformOpsOrAbove")]
    public async Task<IActionResult> Issue([FromBody] IssueLocalLicenseRequest request, CancellationToken cancellationToken)
    {
        if (request.CustomerId is not Guid customerId || customerId == Guid.Empty)
            return BadRequest(new { error = "CustomerId is required." });
        if (request.DeviceLimit <= 0)
            return BadRequest(new { error = "DeviceLimit must be positive." });

        var actor = CurrentPlatformAdminUserId();
        try
        {
            var license = await _licenses.IssueAsync(request, actor, cancellationToken);
            await _audit.LogAsync(actor, "platform.license.issued", after: new { license.Id, license.LicenseKey, license.CustomerId, license.ContractId });
            return CreatedAtAction(nameof(GetDetail), new { id = license.Id }, new { license.Id, license.LicenseKey });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/suspend")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformOpsOrAbove")]
    public async Task<IActionResult> Suspend(Guid id, [FromBody] LicenseActionRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Reason)) return BadRequest(new { error = "Reason is required." });
        var actor = CurrentPlatformAdminUserId();
        var ok = await _licenses.SuspendAsync(id, actor, body.Reason, cancellationToken);
        if (ok) await _audit.LogAsync(actor, "platform.license.suspended", after: new { id, body.Reason });
        return ok ? Ok() : BadRequest(new { error = "License not found or not in a suspendable state." });
    }

    [HttpPost("{id:guid}/revoke")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> Revoke(Guid id, [FromBody] LicenseActionRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Reason)) return BadRequest(new { error = "Reason is required." });
        var actor = CurrentPlatformAdminUserId();
        var ok = await _licenses.RevokeAsync(id, actor, body.Reason, cancellationToken);
        if (ok) await _audit.LogAsync(actor, "platform.license.revoked", after: new { id, body.Reason });
        return ok ? Ok() : BadRequest(new { error = "License not found or not in a revocable state." });
    }

    /// <summary>Revoked/Suspended -> Active is deliberately its own admin-only, reason-required
    /// action - never an implicit side effect of anything else (rule 11's lifecycle section).</summary>
    [HttpPost("{id:guid}/reactivate")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> Reactivate(Guid id, [FromBody] LicenseActionRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Reason)) return BadRequest(new { error = "A documented reason is required." });
        var actor = CurrentPlatformAdminUserId();
        var ok = await _licenses.ReactivateAsync(id, actor, body.Reason, cancellationToken);
        if (ok) await _audit.LogAsync(actor, "platform.license.reactivated", after: new { id, body.Reason });
        return ok ? Ok() : BadRequest(new { error = "License not found or not eligible for reactivation." });
    }

    /// <summary>The authorized PC-replacement workflow (rule 17) — Ops/Admin only, reason
    /// required, fully audited. A Sales Rep can never move a license between installations.</summary>
    [HttpPost("{id:guid}/authorize-transfer")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.AuthenticationScheme, Policy = "PlatformOpsOrAbove")]
    public async Task<IActionResult> AuthorizeTransfer(Guid id, [FromBody] TransferRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Reason)) return BadRequest(new { error = "Reason is required." });
        var actor = CurrentPlatformAdminUserId();
        var ok = await _licenses.AuthorizeTransferAsync(id, body.OldInstallationId, actor, body.Reason, cancellationToken);
        if (ok) await _audit.LogAsync(actor, "platform.license.transfer_authorized", after: new { id, body.OldInstallationId, body.Reason });
        return ok ? Ok() : BadRequest(new { error = "License not found or no matching active installation to release." });
    }

    /// <summary>Ops+/Admin may see the full license secret on list/detail; Sales/Support get a mask.</summary>
    private bool CanRevealLicenseKey()
    {
        if (User.IsInRole(PlatformRoles.Ops) || User.IsInRole(PlatformRoles.Admin))
            return true;
        var role = User.FindFirst("role")?.Value
            ?? User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst(PlatformAuthConstants.RoleClaimType)?.Value;
        return role is PlatformRoles.Ops or PlatformRoles.Admin;
    }
}

public class LicenseActionRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class TransferRequest
{
    /// <summary>Null = release every active installation on this license (rare; normally you
    /// name the specific old installation being replaced).</summary>
    public string? OldInstallationId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
