namespace GMS.Application.DTOs.Invitation;

/// <summary>
/// Live guest-pass quota for a member in the current Cairo calendar month.
/// Usage counts only redeemed visits (<c>VisitedAtUtc</c>), not pending/expired/cancelled sends.
/// </summary>
public class GuestInvitationQuotaDto
{
    public Guid MemberId { get; set; }
    public int TotalGuestInvitations { get; set; }
    public int UsedGuestInvitations { get; set; }
    public int RemainingGuestInvitations { get; set; }
    /// <summary>Cairo period key <c>yyyy-MM</c>.</summary>
    public string QuotaPeriod { get; set; } = string.Empty;
    /// <summary>First Cairo calendar day of the next month (automatic full reset).</summary>
    public DateOnly NextResetDate { get; set; }
    public Guid? PlanId { get; set; }
    public string? PlanName { get; set; }
}
