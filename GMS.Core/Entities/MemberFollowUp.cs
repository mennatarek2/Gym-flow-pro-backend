namespace GMS.Core.Entities;

/// <summary>
/// Operational follow-up queue item. Not the source of truth for membership, payments,
/// attendance, or member identity — those stay on their own tables. Display copy in
/// <see cref="Why"/> is derived at sync time.
/// </summary>
public class MemberFollowUp : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid MemberId { get; set; }
    public Guid? MembershipId { get; set; }

    /// <summary>renewal | trial | payment | welcome | inactive | offer | custom</summary>
    public string Reason { get; set; } = "custom";

    /// <summary>system | manual</summary>
    public string Source { get; set; } = "manual";

    /// <summary>Dedup key, e.g. renewal:{membershipId}. Unique among open rows per tenant.</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>high | medium | low</summary>
    public string Priority { get; set; } = "medium";

    /// <summary>pending | in_progress | contacted | no_answer | completed | cancelled</summary>
    public string Status { get; set; } = "pending";

    /// <summary>app_users.Id</summary>
    public Guid? AssignedToUserId { get; set; }

    public DateTime DueAtUtc { get; set; }
    public string? NextAction { get; set; }
    public DateTime? NextActionAtUtc { get; set; }

    /// <summary>membership | sale | offer</summary>
    public string? RelatedType { get; set; }
    public Guid? RelatedId { get; set; }

    public string? Why { get; set; }
    public string? Notes { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }

    public Tenant? Tenant { get; set; }
    public GymMember? Member { get; set; }
    public Membership? Membership { get; set; }
    public AppUser? AssignedToUser { get; set; }
}
