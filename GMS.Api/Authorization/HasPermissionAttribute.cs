namespace GMS.Api.Authorization;

using Microsoft.AspNetCore.Authorization;

/// <summary>
/// Gates a controller/action behind a single permission, e.g. <c>[HasPermission(Permissions.CheckinManual)]</c>.
/// Resolves to a dynamically-built "Permission:{permission}" policy via <see cref="PermissionPolicyProvider"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission)
        : base(policy: PermissionPolicyProvider.PolicyPrefix + permission)
    {
    }
}
