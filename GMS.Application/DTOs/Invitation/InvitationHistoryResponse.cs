namespace GMS.Application.DTOs.Invitation;

/// <summary>Invitation list item for Member App history and staff lists.</summary>
public class InvitationHistoryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? NationalId { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ContactedAtUtc { get; set; }
    public DateTime? ConvertedAtUtc { get; set; }
    public Guid InvitedByMemberId { get; set; }
    public string InvitedByName { get; set; } = string.Empty;

    /// <summary>Alias for older clients.</summary>
    public string GuestName => Name;
    /// <summary>Alias for older clients.</summary>
    public string GuestPhoneNumber => PhoneNumber;
    public DateTime SentAtUtc => CreatedAtUtc;
}
