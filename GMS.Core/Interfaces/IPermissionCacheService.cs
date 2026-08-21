namespace GMS.Core.Interfaces;

/// <summary>
/// Caches a user's resolved effective permission set so login/refresh don't recompute it every call.
/// Keyed by tenant + user so a role change for one user can be invalidated without affecting others.
/// </summary>
public interface IPermissionCacheService
{
    Task<IReadOnlySet<string>?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    Task SetAsync(Guid tenantId, Guid userId, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default);

    /// <summary>Call whenever a user's roles or permission override changes.</summary>
    Task InvalidateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
}
