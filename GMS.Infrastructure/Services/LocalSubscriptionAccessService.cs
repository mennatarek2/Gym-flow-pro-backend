namespace GMS.Infrastructure.Services;

using GMS.Core.Interfaces;

/// <summary>
/// Local Edition replacement for the SaaS <c>SubscriptionAccessService</c>. A Local install is a
/// single permanently-active install with no billing/suspension concept, so every tenant reports
/// as "active" and never suspended. This is a placeholder for the future perpetual/offline license
/// check (explicitly out of scope for this phase) — not a final license implementation.
/// Registered only when <c>Deployment:Edition</c> resolves to Local (see Program.cs); the real
/// Platform-backed implementation is untouched and still used for SaaS.
/// </summary>
public class LocalSubscriptionAccessService : ISubscriptionAccessService
{
    public Task<SubscriptionAccessSnapshot?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult<SubscriptionAccessSnapshot?>(new SubscriptionAccessSnapshot
        {
            Status = "active",
            SuspendedAtUtc = null,
        });
}
