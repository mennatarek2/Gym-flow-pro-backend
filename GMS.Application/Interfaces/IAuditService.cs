namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Audit;

/// <summary>
/// Reusable audit trail logging. LogAsync is fire-and-forget-safe: it never throws to the caller,
/// even if the underlying write fails (Redis/DB outage, etc.) — failures are logged internally only.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Records an audit event. Uses <paramref name="tenantIdOverride"/> when provided (e.g. anonymous
    /// member-app activate has no <see cref="ITenantContext"/>). Skips write when tenant is missing.
    /// </summary>
    Task LogAsync(
        string action,
        string? entityType = null,
        Guid? entityId = null,
        object? before = null,
        object? after = null,
        Guid? tenantIdOverride = null);

    /// <summary>
    /// Paginated read of audit events for a tenant, with optional filters. Read-only, never throws
    /// the fire-and-forget-safe contract that LogAsync has — callers should handle Result.Failure.
    /// </summary>
    Task<Result<PagedResult<AuditEventDto>>> GetAuditEventsAsync(Guid tenantId, AuditEventQueryRequest query);
}
