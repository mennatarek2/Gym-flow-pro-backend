namespace GMS.Api.Filters;

using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Gates a controller/action behind a per-tenant module flag, e.g. <c>[FeatureFlag("sales")]</c>.
/// An <see cref="IFilterFactory"/> rather than a plain filter, since <see cref="FeatureFlagFilter"/>
/// needs the tenant-scoped DbContext/ITenantContext resolved fresh from DI per request.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class FeatureFlagAttribute : Attribute, IFilterFactory
{
    private readonly string _featureName;

    public FeatureFlagAttribute(string featureName)
    {
        _featureName = featureName;
    }

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        ActivatorUtilities.CreateInstance<FeatureFlagFilter>(serviceProvider, _featureName);
}
