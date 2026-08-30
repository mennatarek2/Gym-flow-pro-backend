namespace GMS.Application.DTOs.Notifications;

using GMS.Core.Constants;

public class StaffNotificationDto
{
    public Guid Id { get; set; }
    public string? Type { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string BodyAr { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Alias of <see cref="SentAtUtc"/> for FE parity with member NotificationDto.SentAt.</summary>
    public DateTime? SentAt { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

public class StaffUnreadCountDto
{
    public int Count { get; set; }
}

/// <summary>Internal publish request — never accepts recipient IDs from public HTTP bodies.</summary>
public class CreateStaffNotificationRequest
{
    public string Type { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = StaffNotificationPriorities.Info;
    public string Title { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string BodyAr { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? ActionUrl { get; set; }
    public string? DedupeKey { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>Explicit AppUser.Id recipients (assignment).</summary>
    public IReadOnlyList<Guid>? RecipientAppUserIds { get; set; }

    /// <summary>Active staff with these AppUser.Role values (Owner/Manager/…).</summary>
    public IReadOnlyList<string>? RecipientRoles { get; set; }

    /// <summary>Active staff whose resolved role permissions include any of these keys.</summary>
    public IReadOnlyList<string>? RecipientPermissions { get; set; }
}
