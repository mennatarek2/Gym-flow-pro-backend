namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Ensures desk staff identity users have a matching tenant-scoped <see cref="AppUser"/> row.
/// Login can succeed without one; cash ledger writes require AppUser.Id for FK columns.
/// </summary>
internal static class StaffAppUserProvisioner
{
    public static async Task<Guid?> ResolveOrCreateAsync(
        GymFlowProDbContext db,
        Guid tenantId,
        Guid identityUserId,
        CancellationToken ct = default)
    {
        var identityKey = identityUserId.ToString();
        var existingId = await db.AppUsers
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenantId
                && !user.IsDeleted
                && user.IsActive
                && (user.Id == identityUserId || user.UserId == identityKey))
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(ct);
        if (existingId.HasValue)
            return existingId;

        var identityUser = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == identityUserId && user.TenantId == tenantId, ct);
        if (identityUser == null || !identityUser.IsActive)
            return null;

        var role = await db.UserRoles
            .Where(item => item.UserId == identityUserId)
            .Join(db.Roles, item => item.RoleId, role => role.Id, (_, role) => role.Name)
            .FirstOrDefaultAsync(ct) ?? "Staff";

        var appUser = new AppUser
        {
            TenantId = tenantId,
            UserId = identityKey,
            FirstName = identityUser.FirstName,
            LastName = identityUser.LastName,
            Email = identityUser.Email ?? string.Empty,
            PhoneNumber = identityUser.PhoneNumber ?? string.Empty,
            Role = role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.AppUsers.Add(appUser);
        try
        {
            await db.SaveChangesAsync(ct);
            return appUser.Id;
        }
        catch (DbUpdateException)
        {
            return await db.AppUsers
                .IgnoreQueryFilters()
                .Where(user => user.TenantId == tenantId
                    && !user.IsDeleted
                    && user.UserId == identityKey)
                .Select(user => (Guid?)user.Id)
                .FirstOrDefaultAsync(ct);
        }
    }
}
