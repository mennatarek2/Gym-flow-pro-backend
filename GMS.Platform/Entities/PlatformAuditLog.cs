namespace GMS.Platform.Entities;

/// <summary>Append-only platform audit trail (cross-tenant actions).</summary>
public class PlatformAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorPlatformUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
