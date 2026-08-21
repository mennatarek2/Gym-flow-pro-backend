namespace GMS.Api.Authorization;

using Microsoft.AspNetCore.Authorization;

/// <summary>
/// Requirement satisfied when the current principal carries a "perm" claim matching <see cref="Permission"/>.
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}
