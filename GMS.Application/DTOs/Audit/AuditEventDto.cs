namespace GMS.Application.DTOs.Audit;

public class AuditEventDto
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? IpAddress { get; set; }
    public Guid? ImpersonatedByPlatformUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
