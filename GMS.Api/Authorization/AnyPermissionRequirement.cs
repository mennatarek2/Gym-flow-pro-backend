namespace GMS.Api.Authorization;

using Microsoft.AspNetCore.Authorization;

public sealed class AnyPermissionRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; }

    public AnyPermissionRequirement(IEnumerable<string> permissions)
    {
        Permissions = permissions.ToArray();
    }
}
