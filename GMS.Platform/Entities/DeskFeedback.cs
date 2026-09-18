namespace GMS.Platform.Entities;

/// <summary>
/// Product feedback submitted from a gym desk (Owner/Manager/Receptionist/Trainer).
/// Lives on the platform plane so HyMotion support can review it. Distinct from
/// <see cref="PlatformSupportTicket"/> (ops-created operational tickets).
/// </summary>
public class DeskFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Optional link when the gym maps to a PlatformCustomer (TenantId or Local license).</summary>
    public Guid? CustomerId { get; set; }

    public Guid TenantId { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }

    public Guid SenderUserId { get; set; }
    public string SenderRole { get; set; } = string.Empty;
    public string? SenderEmail { get; set; }
    public string? SenderDisplayName { get; set; }

    public string Category { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;

    public string Status { get; set; } = "new";
    public string? AppVersion { get; set; }
    public string? PageUrl { get; set; }

    /// <summary>Client-supplied idempotency key (optional). Unique per tenant when set.</summary>
    public string? ClientRequestId { get; set; }

    public string? InternalNote { get; set; }
    public string? ResponseToCustomer { get; set; }

    public Guid? ReviewedByPlatformAdminUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public PlatformCustomer? Customer { get; set; }
}
