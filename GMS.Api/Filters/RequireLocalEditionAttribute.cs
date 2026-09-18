namespace GMS.Api.Filters;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using GMS.Core.Configuration;

/// <summary>
/// Gates a controller/action to Local Edition only — 404s on SaaS. Implemented as a resource
/// filter (not an action filter like FeatureFlagFilter) specifically because it must run before
/// [ApiController]'s automatic model-validation 400, so a SaaS request with an invalid/missing
/// body still gets a clean 404 instead of leaking a 400 that reveals the endpoint's request shape.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireLocalEditionAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        ActivatorUtilities.CreateInstance<RequireLocalEditionFilter>(serviceProvider);
}

public class RequireLocalEditionFilter : IAsyncResourceFilter
{
    private readonly DeploymentEdition _edition;

    public RequireLocalEditionFilter(DeploymentEdition edition)
    {
        _edition = edition;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (_edition != DeploymentEdition.Local)
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }
}
