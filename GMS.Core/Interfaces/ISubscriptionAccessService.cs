namespace GMS.Core.Interfaces;

/// <summary>
/// Hot-path subscription access snapshot for tenant auth / middleware (CP5 suspension gate).
/// Implemented by Platform; consumed by Application + Api without referencing Platform types.
/// </summary>
public interface ISubscriptionAccessService
{
    Task<SubscriptionAccessSnapshot?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class SubscriptionAccessSnapshot
{
    public string Status { get; init; } = string.Empty;
    public DateTime? SuspendedAtUtc { get; init; }

    public bool IsSuspended =>
        string.Equals(Status, "suspended", StringComparison.OrdinalIgnoreCase);
}
