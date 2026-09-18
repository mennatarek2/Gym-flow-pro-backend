namespace GMS.Infrastructure.Services;

using GMS.Core.Interfaces;

/// <summary>
/// Local Edition replacement for the SaaS <c>FeatureAccessService</c>. A Local install has a single
/// permanent, unrestricted feature set (no tiers, no billing) — every feature is enabled.
/// Registered only when <c>Deployment:Edition</c> resolves to Local (see Program.cs); the real
/// Platform-backed implementation is untouched and still used for SaaS.
/// </summary>
public class LocalFeatureAccessService : IFeatureAccessService
{
    public Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
