namespace GMS.Application.DTOs.Invitation;

/// <summary>Create Invitation result. Quota figures are server-calculated.</summary>
public class SendInvitationResponse
{
    public Guid InvitationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool AlreadyExisted { get; set; }
    public int QuotaTotal { get; set; }
    public int QuotaUsed { get; set; }
    public int QuotaRemaining { get; set; }
    public string Message { get; set; } = string.Empty;
    public string MessageAr { get; set; } = string.Empty;

    /// <summary>Alias for older clients.</summary>
    public string GuestName => Name;
}
