namespace GMS.Api.Authorization;

using Microsoft.AspNetCore.Authorization;
using GMS.Core.Constants;

public sealed class AnyPermissionAuthorizationHandler : AuthorizationHandler<AnyPermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AnyPermissionRequirement requirement)
    {
        if (requirement.Permissions.Any(permission =>
                context.User.HasClaim(Permissions.ClaimType, permission)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
