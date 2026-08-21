namespace GMS.Api.Filters;

using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using GMS.Platform.Constants;

/// <summary>
/// Hangfire dashboard authorization.
/// Development: open for local ops.
/// Production: Platform Administrators only (PlatformBearer + platform_admin). Tenant Owners/staff blocked.
/// </summary>
public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var env = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();

        if (env.IsDevelopment())
            return true;

        // Prefer PlatformBearer — default JWT scheme is tenant-facing and must not grant Hangfire access.
        var platformAuth = httpContext
            .AuthenticateAsync(PlatformAuthConstants.AuthenticationScheme)
            .GetAwaiter()
            .GetResult();

        if (!platformAuth.Succeeded || platformAuth.Principal?.Identity?.IsAuthenticated != true)
            return false;

        return platformAuth.Principal.IsInRole(PlatformRoles.Admin);
    }
}
