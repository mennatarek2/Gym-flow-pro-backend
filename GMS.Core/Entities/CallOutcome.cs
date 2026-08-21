namespace GMS.Core.Entities;

/// <summary>
/// Append-only interaction history for a follow-up (or a legacy membership-keyed call).
/// Never the source of truth for membership / payment / attendance status.
/// </summary>
public class CallOutcome : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid? FollowUpId { get; set; }
    public Guid? MemberId { get; set; }
    public Guid? MembershipId { get; set; }

    /// <summary>app_users.Id — the staff member who logged the attempt.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// reached | no_answer | busy | wrong_number | not_interested | will_visit | renewed | needs_follow_up
    /// Legacy: contacted | declined
    /// </summary>
    public string Outcome { get; set; } = string.Empty;

    public string? Note { get; set; }
    public string? NextAction { get; set; }
    public DateTime? NextActionAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public MemberFollowUp? FollowUp { get; set; }
    public GymMember? Member { get; set; }
    public Membership? Membership { get; set; }
    public AppUser? User { get; set; }
}
