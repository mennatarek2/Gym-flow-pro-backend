namespace GMS.Tests.Helpers;

using GMS.Core.Interfaces;

internal sealed class AlwaysEnabledFeatureAccess : IFeatureAccessService
{
    public Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

internal sealed class UnlimitedTierEnforcement : ITierEnforcementService
{
    public Task<CapCheckResult> CheckCapAsync(Guid tenantId, string metric, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CapCheckResult
        {
            Allowed = true,
            SoftWarning = false,
            Count = 0,
            Cap = null,
            Metric = metric
        });
}
