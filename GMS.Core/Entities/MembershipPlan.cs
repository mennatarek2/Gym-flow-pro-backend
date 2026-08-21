namespace GMS.Core.Entities;

/// <summary>
/// Represents a membership plan offered by the gym.
/// Supports multiple plan types: unlimited, session packs, time-limited, PT credits, family plans.
/// </summary>
public class MembershipPlan : BaseEntity
{
    // Tenant context
    public Guid TenantId { get; set; }

    // Plan information
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DescriptionAr { get; set; } = string.Empty;

    // Plan type
    public string PlanType { get; set; } = "monthly_unlimited";
    // Valid values: 'monthly_unlimited', 'session_pack', 'time_limited', 'pt_credits', 'family', 'trial', 'day_pass'

    // Duration & Sessions
    public int DurationDays { get; set; }
    public int? SessionCount { get; set; } // For session-pack plans

    /// <summary>Visit cap for trial plans that expire by visit count instead of (or in addition to) date. Null = no visit cap.</summary>
    public int? TrialVisitLimit { get; set; }

    // Pricing
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EGP";

    // Time restrictions
    public TimeOnly? TimeRestrictionStart { get; set; }
    public TimeOnly? TimeRestrictionEnd { get; set; }

    // Guest invitations (guest_pass monthly quota) — retired product; keep column for history.
    public int InvitationQuota { get; set; } = 0;

    /// <summary>
    /// Invitations a member may create during one covering membership on this plan.
    /// Consumed on create. No carry-over on renew. Frozen/expired/cancelled → 0 remaining.
    /// </summary>
    public int ReferralInviteQuota { get; set; } = 0;

    /// <summary>credit | free_days — null = owner has not configured (INV-4 defaults by price).</summary>
    public string? ReferralRewardType { get; set; }

    /// <summary>EGP amount when credit, or day count when free_days.</summary>
    public decimal? ReferralRewardValue { get; set; }

    // Status
    public bool IsActive { get; set; } = true;

    // Timestamps
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<PlanEntitlement> Entitlements { get; set; } = new List<PlanEntitlement>();
}
