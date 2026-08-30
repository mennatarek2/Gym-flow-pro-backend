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

    /// <summary>Optional reference to the external message ID (4jawaly, FCM) or staff dedupe key.</summary>
    public string? ExternalMessageId { get; set; }

    /// <summary>Typed staff event key, e.g. membership.expired. Null for legacy member/bulk rows.</summary>
    public string? Type { get; set; }

    /// <summary>Staff inbox category (Members, Payments, …). Null for legacy member/bulk rows.</summary>
    public string? Category { get; set; }

    /// <summary>Critical | ActionRequired | Info. Null for legacy member/bulk rows.</summary>
    public string? Priority { get; set; }

    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    /// <summary>Relative desk path for CTA, e.g. /dashboard/members/{id}/.</summary>
    public string? ActionUrl { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public GymMember? Member { get; set; }
    public AppUser? AppUser { get; set; }
}
