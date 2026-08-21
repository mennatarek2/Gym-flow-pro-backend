namespace GMS.Application.DTOs.Notifications;

/// <summary>
/// Request DTO for sending bulk notifications.
/// </summary>
public class SendBulkNotificationRequest
{
    /// <summary>Specific member IDs to notify. Null = use AllMembers flag.</summary>
    public List<Guid>? MemberIds { get; set; }

    /// <summary>If true, notify all active members in the tenant.</summary>
    public bool AllMembers { get; set; }

    public string Title { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string BodyAr { get; set; } = string.Empty;

    /// <summary>Channel: "push" or "whatsapp".</summary>
    public string Channel { get; set; } = "push";
}
