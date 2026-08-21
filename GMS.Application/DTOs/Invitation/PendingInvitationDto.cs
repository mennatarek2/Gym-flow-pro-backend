namespace GMS.Application.DTOs.Invitation;

/// <summary>Desk search result for pending guest_pass invitations.</summary>
public class PendingInvitationDto
{
    public Guid Id { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string GuestPhoneNumber { get; set; } = string.Empty;
    public DateOnly? VisitDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string InvitationType { get; set; } = string.Empty;
    public Guid InvitingMemberId { get; set; }
    public string InvitingMemberName { get; set; } = string.Empty;
    public string InvitingMemberNumber { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
}

/// <summary>Response after desk redeems a guest visit.</summary>
public class RedeemInvitationVisitResponse
{
    public Guid InvitationId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string GuestPhoneNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "visited";
    public DateTime VisitedAtUtc { get; set; }
    public string InvitingMemberName { get; set; } = string.Empty;
    /// <summary>Guest invites consumed this Cairo month after this redeem (visited count).</summary>
    public int QuotaUsed { get; set; }
    public int QuotaRemaining { get; set; }
    public string Message { get; set; } = string.Empty;
    public string MessageAr { get; set; } = string.Empty;
}
