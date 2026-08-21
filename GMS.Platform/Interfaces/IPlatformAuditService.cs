namespace GMS.Platform.Interfaces;

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
}
