namespace GMS.Api.Filters;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using GMS.Core.Interfaces;

/// <summary>
/// Gates an action/controller behind a per-tenant module flag via <see cref="IFeatureAccessService"/>.
/// When the flag is off, returns a 404 ProblemDetails with title "FEATURE_DISABLED".
/// </summary>
public class FeatureFlagFilter : IAsyncActionFilter
{
    private readonly string _featureName;
    private readonly IFeatureAccessService _featureAccess;
    private readonly ITenantContext _tenantContext;

    public FeatureFlagFilter(
        string featureName,
        IFeatureAccessService featureAccess,
        ITenantContext tenantContext)
    {
        _featureName = featureName;
        _featureAccess = featureAccess;
        _tenantContext = tenantContext;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (_tenantContext.IsInitialized)
        {
            var enabled = await _featureAccess.IsEnabledAsync(_tenantContext.TenantId, _featureName);
            if (!enabled)
            {
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Title = "FEATURE_DISABLED",
                    Detail = $"The '{_featureName}' module is disabled for this account / تم تعطيل وحدة '{_featureName}' لهذا الحساب",
                    Status = StatusCodes.Status404NotFound
                })
                {
                    StatusCode = StatusCodes.Status404NotFound
                };
                return;
            }
        }

        await next();
    }
}
