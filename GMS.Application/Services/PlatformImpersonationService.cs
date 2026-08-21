namespace GMS.Application.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Platform.DTOs;
using GMS.Platform.Interfaces;

/// <summary>
/// Mints a short-lived tenant JWT flagged with impersonated_by_platform_user_id.
/// No refresh token is issued — impersonation cannot be renewed.
/// </summary>
public class PlatformImpersonationService : IPlatformImpersonationService
{
    private readonly GymFlowProDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IPermissionProvider _permissionProvider;
    private readonly IPlatformAuditService _audit;
    private readonly ILogger<PlatformImpersonationService> _logger;

    public PlatformImpersonationService(
        GymFlowProDbContext db,
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IPermissionProvider permissionProvider,
        IPlatformAuditService audit,
        ILogger<PlatformImpersonationService> logger)
    {
        _db = db;
        _userManager = userManager;
        _tokenService = tokenService;
        _permissionProvider = permissionProvider;
        _audit = audit;
        _logger = logger;
    }

    public async Task<ImpersonationCreateResult> CreateAsync(
        Guid tenantId,
        Guid platformUserId,
        string reason,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, cancellationToken);

        if (tenant == null)
            return ImpersonationCreateResult.Fail("TENANT_NOT_FOUND", "Tenant not found.");

        var owners = await _userManager.GetUsersInRoleAsync("Owner");
        var owner = owners.FirstOrDefault(u => u.TenantId == tenantId && u.IsActive);
        if (owner == null)
            return ImpersonationCreateResult.Fail("OWNER_NOT_FOUND", "No active Owner user for this tenant.");

        var roles = await _userManager.GetRolesAsync(owner);
        var overlay = RolePermissionResolver.ParseOverlay(tenant.Settings);
        var permissions = RolePermissionResolver.Resolve(roles, _permissionProvider, overlay);

        var expiresAt = DateTime.UtcNow.AddMinutes(ImpersonationClaims.LifetimeMinutes);
        var accessToken = await _tokenService.GenerateImpersonationAccessTokenAsync(
            owner,
            tenantId,
            tenant.GymCode,
            roles,
            permissions,
            platformUserId,
            ImpersonationClaims.LifetimeMinutes);

        await _audit.LogAsync(
            platformUserId,
            "platform.tenant.impersonate",
            tenantId,
            before: null,
            after: new
            {
                Reason = reason,
                ImpersonatedUserId = owner.Id,
                ImpersonatedEmail = owner.Email,
                ExpiresAtUtc = expiresAt,
                LifetimeMinutes = ImpersonationClaims.LifetimeMinutes
            },
            ipAddress);

        _logger.LogInformation(
            "Platform user {PlatformUserId} started impersonation of tenant {TenantId} (owner {OwnerId}) until {ExpiresAtUtc}",
            platformUserId, tenantId, owner.Id, expiresAt);

        return ImpersonationCreateResult.Ok(new ImpersonationResponse
        {
            AccessToken = accessToken,
            ExpiresAtUtc = expiresAt,
            TenantId = tenantId,
            GymCode = tenant.GymCode,
            ImpersonatedUserId = owner.Id,
            ImpersonatedEmail = owner.Email ?? string.Empty
        });
    }
}
