namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Admin;

public interface IRolePermissionService
{
    Task<Result<RoleCatalogDto>> GetCatalogAsync(Guid tenantId);

    Task<Result<RoleAccessDto>> UpdateRoleAsync(Guid tenantId, string role, UpdateRolePermissionsRequest request);

    Task<Result<RoleAccessDto>> ResetRoleAsync(Guid tenantId, string role);
}
