namespace GMS.Api.Authorization;

using Microsoft.AspNetCore.Authorization;
using GMS.Core.Constants;

/// <summary>
/// Grants a <see cref="PermissionRequirement"/> when the JWT carries a matching "perm" claim.
/// Permissions are resolved once at login (see AuthService.ResolvePermissionsAsync) and baked
/// into the token, so this handler is a pure claims check — no DB/Redis call per request.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.HasClaim(Permissions.ClaimType, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
