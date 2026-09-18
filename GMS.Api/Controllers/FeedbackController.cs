namespace GMS.Api.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using GMS.Core.Configuration;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>
/// Gym-desk product feedback ingest. On SaaS, persists on the platform plane for HyMotion review.
/// On Local, forwards to the license server (HyMotion cloud) so Platform Console can receive it.
/// Tenant and sender identity come from the authenticated context — never from the body.
/// </summary>
[ApiController]
[Route("api/feedback")]
[Authorize(Roles = "Owner,Manager,Receptionist,Trainer")]
public class FeedbackController : BaseApiController
{
    private readonly IDeskFeedbackService _feedback;
    private readonly ITenantContext _tenantContext;
    private readonly GymFlowProDbContext _gymDb;
    private readonly DeploymentEdition _edition;
    private readonly ILocalLicenseClientService _licenseClient;

    public FeedbackController(
        IDeskFeedbackService feedback,
        ITenantContext tenantContext,
        GymFlowProDbContext gymDb,
        DeploymentEdition edition,
        ILocalLicenseClientService licenseClient)
    {
        _feedback = feedback;
        _tenantContext = tenantContext;
        _gymDb = gymDb;
        _edition = edition;
        _licenseClient = licenseClient;
    }

    [HttpPost]
    [EnableRateLimiting("feedback-policy")]
    public async Task<IActionResult> Submit([FromBody] SubmitDeskFeedbackRequest request, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized || _tenantContext.TenantId == Guid.Empty)
            return Unauthorized(new { error = "Tenant context is required." });

        var userId = ResolveUserId();
        if (userId == null)
            return Unauthorized(new { error = "User context is required." });

        var role = User.FindFirst(ClaimTypes.Role)?.Value
                   ?? User.FindFirst("role")?.Value
                   ?? "staff";

        var email = User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
                    ?? User.FindFirst(ClaimTypes.Email)?.Value;

        string? displayName = null;
        string? gymCode = null;
        string? gymName = _tenantContext.TenantName;

        var user = await _gymDb.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId.Value && u.TenantId == _tenantContext.TenantId, ct);
        if (user != null)
        {
            email ??= user.Email;
            displayName = $"{user.FirstName} {user.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(displayName)) displayName = null;
        }

        var tenant = await _gymDb.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, ct);
        if (tenant != null)
        {
            gymCode = string.IsNullOrWhiteSpace(tenant.GymCode) ? null : tenant.GymCode;
            gymName = string.IsNullOrWhiteSpace(tenant.Name) ? gymName : tenant.Name;
        }

        var body = request ?? new SubmitDeskFeedbackRequest();

        if (_edition == DeploymentEdition.Local)
        {
            var forwarded = await _licenseClient.ReportDeskFeedbackAsync(
                new LocalDeskFeedbackReportRequest
                {
                    TenantId = _tenantContext.TenantId,
                    SenderUserId = userId.Value,
                    SenderRole = role,
                    SenderEmail = email,
                    SenderDisplayName = displayName,
                    GymCode = gymCode,
                    GymName = gymName,
                    Category = body.Category,
                    Subject = body.Subject,
                    Message = body.Message,
                    AppVersion = body.AppVersion,
                    PageUrl = body.PageUrl,
                    ClientRequestId = body.ClientRequestId,
                },
                ct);

            if (!forwarded.Success)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = forwarded.ErrorCode ?? "unreachable",
                    message = forwarded.ErrorMessage
                        ?? "Could not reach HyMotion. Check the internet connection and try again.",
                });
            }

            return Ok(new
            {
                id = forwarded.Id,
                alreadySubmitted = forwarded.AlreadySubmitted,
                status = "new",
                category = body.Category,
                message = body.Message,
            });
        }

        var actor = new DeskFeedbackActorContext
        {
            TenantId = _tenantContext.TenantId,
            GymCode = gymCode,
            GymName = gymName,
            SenderUserId = userId.Value,
            SenderRole = role,
            SenderEmail = email,
            SenderDisplayName = displayName,
        };

        try
        {
            var created = await _feedback.SubmitAsync(body, actor, ct);
            return Ok(created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    Guid? ResolveUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
