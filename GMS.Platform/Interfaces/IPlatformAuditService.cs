namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

public interface IPlatformAuditService
{
    /// <summary>
    /// Fire-and-forget-safe: never throws to the caller. Redacts [Redact] properties like tenant AuditService.
    /// </summary>
    Task LogAsync(
        Guid actorPlatformUserId,
        string action,
        Guid? tenantId = null,
        object? before = null,
        object? after = null,
        string? ipAddress = null);

    /// <summary>
    /// Cross-tenant audit feed (the same platform_audit_log table the per-tenant RecentAudit reads from —
    /// not a second audit system). Newest first. <paramref name="tenantId"/> narrows to one tenant;
    /// omit for the global feed. <paramref name="from"/>/<paramref name="to"/> are inclusive Cairo-agnostic
    /// UTC calendar days (platform actions aren't tenant-timezone-scoped).
    /// </summary>
    Task<PlatformPagedResult<PlatformAuditLogDto>> ListAsync(
        Guid? tenantId,
        string? action,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
