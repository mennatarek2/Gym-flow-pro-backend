namespace GMS.Platform.Entities;

/// <summary>
/// Observational Local lifecycle report authenticated by license key + installation id.
/// Not a workflow engine. Support uses OperationId to reconstruct one New Gym / restore / recovery.
/// </summary>
public class LocalLifecycleEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public Guid? LicenseId { get; set; }
    public string? InstallationId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
