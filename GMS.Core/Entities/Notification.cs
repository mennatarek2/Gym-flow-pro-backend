namespace GMS.Core.Entities;

/// <summary>
/// Represents an in-app/push/WhatsApp notification sent to a member.
/// Stored for history, read-status tracking, and audit.
/// </summary>
public class Notification : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Recipient when this is a member-facing notification. Exactly one of MemberId/AppUserId is set.</summary>
    public Guid? MemberId { get; set; }

    /// <summary>Recipient (app_users.Id) when this is a staff-facing notification. Exactly one of MemberId/AppUserId is set.</summary>
    public Guid? AppUserId { get; set; }

    /// <summary>Notification channel: "push", "whatsapp", "in_app".</summary>
    public string Channel { get; set; } = "in_app";

    public string Title { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string BodyAr { get; set; } = string.Empty;

    /// <summary>Delivery status: "pending", "sent", "delivered", "failed".</summary>
    public string Status { get; set; } = "pending";

    public DateTime? SentAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }

    /// <summary>Optional reference to the external message ID (4jawaly, FCM).</summary>
    public string? ExternalMessageId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public GymMember? Member { get; set; }
    public AppUser? AppUser { get; set; }
}
