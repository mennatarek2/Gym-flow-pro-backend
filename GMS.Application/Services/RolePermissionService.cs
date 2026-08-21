namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Admin;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

public class RolePermissionService : IRolePermissionService
{
    private static readonly string[] Notes =
    {
        "Takes effect on next login, or within about 15 minutes if they stay signed in.",
        "Gym Settings, Staff, and Roles stay Owner-only. Ticking Manage gym settings opens Import and Audit, not Gym Identity.",
        "Receptionist is not included in some staff-only screens (AnyStaff). Ticking a task does not open those.",
        "Owner and Member cannot be changed."
    };

    private const string LockedMessage =
        "ROLE_LOCKED|This job cannot be edited / لا يمكن تعديل هذا الدور";
    private const string UnknownMessage = "Unknown role / دور غير معروف";
    private const string TenantMissing = "Tenant not found / المنظمة غير موجودة";

    private readonly GymFlowProDbContext _db;
    private readonly IPermissionProvider _provider;
    private readonly IPermissionCacheService _cache;
    private readonly IAuditService _audit;
    private readonly ILogger<RolePermissionService> _logger;

    public RolePermissionService(
        GymFlowProDbContext db,
        IPermissionProvider provider,
        IPermissionCacheService cache,
        IAuditService audit,
        ILogger<RolePermissionService> logger)
    {
        _db = db;
        _provider = provider;
        _cache = cache;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<RoleCatalogDto>> GetCatalogAsync(Guid tenantId)
    {
        try
        {
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                return Result<RoleCatalogDto>.Failure(TenantMissing);

            var overlay = RolePermissionResolver.ParseOverlay(tenant.Settings);
            var catalog = new RoleCatalogDto
            {
                Universe = Permissions.All.ToList(),
                Notes = Notes.ToList(),
                EffectCopy = Notes[0]
            };

            foreach (var role in RolePermissionResolver.StaffRoles)
                catalog.Roles.Add(BuildRole(role, overlay));

            return Result<RoleCatalogDto>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load role catalog for {TenantId}", tenantId);
            return Result<RoleCatalogDto>.Failure("Could not load roles / تعذر تحميل الأدوار");
        }
    }

    public async Task<Result<RoleAccessDto>> UpdateRoleAsync(
        Guid tenantId, string role, UpdateRolePermissionsRequest request)
    {
        var canonical = RolePermissionResolver.CanonicalRole(role);
        if (canonical is "Owner" or "Member")
            return Result<RoleAccessDto>.Failure(LockedMessage);
        if (!RolePermissionResolver.IsEditable(canonical))
            return Result<RoleAccessDto>.Failure(UnknownMessage);

        try
        {
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                return Result<RoleAccessDto>.Failure(TenantMissing);

            var overlay = RolePermissionResolver.ParseOverlay(tenant.Settings);
            var defaults = _provider.GetPermissions(new[] { canonical });
            var normalized = RolePermissionResolver.NormalizeKeys(request.Permissions ?? new List<string>());
            var before = overlay.TryGetValue(canonical, out var prev)
                ? prev.ToList()
                : defaults.ToList();

            if (RolePermissionResolver.SameSet(normalized, defaults))
                overlay.Remove(canonical);
            else
                overlay[canonical] = normalized;

            tenant.Settings = RolePermissionResolver.WriteOverlay(tenant.Settings, overlay);
            tenant.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await InvalidateRoleAsync(tenantId, canonical);

            var after = BuildRole(canonical, overlay);
            await _audit.LogAsync(
                "roles.update",
                "Role",
                tenantId,
                new { role = canonical, permissions = before },
                new { role = canonical, permissions = after.Permissions, isCustomized = after.IsCustomized });

            _logger.LogInformation("Role {Role} overlay updated for tenant {TenantId}", canonical, tenantId);
            return Result<RoleAccessDto>.Success(after);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update role {Role} for {TenantId}", canonical, tenantId);
            return Result<RoleAccessDto>.Failure("Could not save this job / تعذر حفظ الدور");
        }
    }

    public async Task<Result<RoleAccessDto>> ResetRoleAsync(Guid tenantId, string role)
    {
        var canonical = RolePermissionResolver.CanonicalRole(role);
        if (canonical is "Owner" or "Member")
            return Result<RoleAccessDto>.Failure(LockedMessage);
        if (!RolePermissionResolver.IsEditable(canonical))
            return Result<RoleAccessDto>.Failure(UnknownMessage);

        try
        {
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                return Result<RoleAccessDto>.Failure(TenantMissing);

            var overlay = RolePermissionResolver.ParseOverlay(tenant.Settings);
            var before = overlay.TryGetValue(canonical, out var prev)
                ? prev.ToList()
                : _provider.GetPermissions(new[] { canonical }).ToList();

            overlay.Remove(canonical);
            tenant.Settings = RolePermissionResolver.WriteOverlay(tenant.Settings, overlay);
            tenant.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await InvalidateRoleAsync(tenantId, canonical);

            var after = BuildRole(canonical, overlay);
            await _audit.LogAsync(
                "roles.reset",
                "Role",
                tenantId,
                new { role = canonical, permissions = before },
                new { role = canonical, permissions = after.Permissions, isCustomized = false });

            return Result<RoleAccessDto>.Success(after);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset role {Role} for {TenantId}", canonical, tenantId);
            return Result<RoleAccessDto>.Failure("Could not reset this job / تعذر إعادة الدور");
        }
    }

    private RoleAccessDto BuildRole(string role, IReadOnlyDictionary<string, IReadOnlyList<string>> overlay)
    {
        var defaults = _provider.GetPermissions(new[] { role }).ToList();
        defaults = RolePermissionResolver.NormalizeKeys(defaults).ToList();
        var customized = overlay.ContainsKey(role);
        var effective = customized
            ? overlay[role].ToList()
            : defaults;
        return new RoleAccessDto
        {
            Id = role,
            Editable = RolePermissionResolver.IsEditable(role),
            IsCustomized = customized,
            Defaults = defaults,
            Permissions = effective
        };
    }

    private async Task InvalidateRoleAsync(Guid tenantId, string role)
    {
        var rows = await _db.AppUsers.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .Select(a => new { a.UserId, a.Role })
            .ToListAsync();

        foreach (var row in rows)
        {
            if (!string.Equals(
                    RolePermissionResolver.CanonicalRole(row.Role),
                    role,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (Guid.TryParse(row.UserId, out var userId))
                await _cache.InvalidateAsync(tenantId, userId);
        }
    }
}
