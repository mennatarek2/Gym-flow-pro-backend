namespace GMS.Api.Filters;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using GMS.Core.Constants;

/// <summary>
/// Rejects requests authenticated with a platform-support impersonation JWT.
/// Apply to endpoints that require the real owner's non-impersonated identity
/// (password reset, ownership-critical settings, etc.).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RejectImpersonationAttribute : Attribute, IAsyncActionFilter
{
    public const string ErrorCode = "IMPERSONATION_FORBIDDEN";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (ImpersonationPrincipal.IsImpersonating(context.HttpContext.User))
        {
            context.Result = new ObjectResult(new
            {
                errorCode = ErrorCode,
                message = "This action requires the account owner's own credentials; GymFlow Support impersonation cannot perform it."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}

public static class ImpersonationPrincipal
{
    public static bool IsImpersonating(System.Security.Claims.ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        var claim = user.FindFirst(ImpersonationClaims.ImpersonatedByPlatformUserId)?.Value
                    ?? user.FindFirst(ImpersonationClaims.TokenUse)?.Value;
        if (string.IsNullOrEmpty(claim))
            return false;

        if (string.Equals(
                user.FindFirst(ImpersonationClaims.TokenUse)?.Value,
                ImpersonationClaims.TokenUseImpersonation,
                StringComparison.OrdinalIgnoreCase))
            return true;

        return Guid.TryParse(
            user.FindFirst(ImpersonationClaims.ImpersonatedByPlatformUserId)?.Value,
            out _);
    }

    public static Guid? GetPlatformUserId(System.Security.Claims.ClaimsPrincipal? user)
    {
        var raw = user?.FindFirst(ImpersonationClaims.ImpersonatedByPlatformUserId)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
