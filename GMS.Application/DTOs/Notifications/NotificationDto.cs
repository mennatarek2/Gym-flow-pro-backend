namespace GMS.Application.DTOs.Notifications;

/// <summary>
/// Notification DTO for member notification list.
/// </summary>
public class NotificationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string BodyAr { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
    public bool IsRead { get; set; }
}
