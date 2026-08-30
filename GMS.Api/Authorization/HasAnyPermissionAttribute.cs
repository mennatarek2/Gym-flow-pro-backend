namespace GMS.Api.Authorization;

using Microsoft.AspNetCore.Authorization;

/// <summary>
/// Allows an endpoint to preserve legacy access while granting a narrower
/// dedicated permission to roles such as Trainer.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class HasAnyPermissionAttribute : AuthorizeAttribute
{
    public HasAnyPermissionAttribute(params string[] permissions)
        : base(policy: PermissionPolicyProvider.AnyPolicyPrefix + string.Join("|", permissions))
    {
    }
}
