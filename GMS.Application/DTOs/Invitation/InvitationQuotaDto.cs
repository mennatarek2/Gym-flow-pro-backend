namespace GMS.Application.DTOs.Invitation;

/// <summary>
/// Server-calculated Invitation quota for the covering membership.
/// Frozen / expired / cancelled / no membership → remaining 0.
/// </summary>
public class InvitationQuotaDto
{
    public Guid MemberId { get; set; }
    public Guid? MembershipId { get; set; }
    public Guid? PlanId { get; set; }
    public string? PlanName { get; set; }
    public int Total { get; set; }
    public int Used { get; set; }
    public int Remaining { get; set; }
    public string MembershipStatus { get; set; } = "none";
}
