namespace GMS.Platform.Entities;

/// <summary>
/// Generic automation enrollment (CP5 / future Phase B WS15).
/// One active row per (SequenceKey, SubjectType, SubjectId); halt clears the active unique slot.
/// </summary>
public class AutomationEnrollment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>e.g. platform_invoice_dunning</summary>
    public string SequenceKey { get; set; } = string.Empty;

    /// <summary>member | platform_invoice</summary>
    public string SubjectType { get; set; } = string.Empty;

    public Guid SubjectId { get; set; }

    /// <summary>Owning tenant when known (platform invoice / member retention).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>0-based step index within the sequence definition.</summary>
    public int Step { get; set; }

    public DateTime NextRunAtUtc { get; set; }

    /// <summary>Null while active; set when halted (paid / completed / cancelled).</summary>
    public string? HaltedReason { get; set; }

    public DateTime? HaltedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
