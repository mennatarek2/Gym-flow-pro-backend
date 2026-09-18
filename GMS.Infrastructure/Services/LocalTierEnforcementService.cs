namespace GMS.Infrastructure.Services;

using GMS.Core.Interfaces;

/// <summary>
/// Local Edition replacement for the SaaS <c>TierEnforcementService</c>. A Local install has no
/// plan/seat caps to enforce, so every metric check reports unlimited allowance.
/// Registered only when <c>Deployment:Edition</c> resolves to Local (see Program.cs); the real
/// Platform-backed implementation is untouched and still used for SaaS.
/// </summary>
public class LocalTierEnforcementService : ITierEnforcementService
{
    public Task<CapCheckResult> CheckCapAsync(Guid tenantId, string metric, CancellationToken cancellationToken = default)
        => Task.FromResult(new CapCheckResult
        {
            Allowed = true,
            SoftWarning = false,
            Count = 0,
            Cap = null,
            Metric = metric,
        });
}
