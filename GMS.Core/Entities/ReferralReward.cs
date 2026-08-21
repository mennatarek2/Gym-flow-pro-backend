namespace GMS.Core.Entities;

/// <summary>
/// Dual-sided referral reward lifecycle: fraud hold → grant / forfeit / reverse.
/// Ledger grants reuse <see cref="MemberCredit"/> with EntryType referral_reward.
/// </summary>
public class ReferralReward : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid InvitationId { get; set; }
    public Guid? SaleId { get; set; }

    /// <summary>Member who receives this reward (referrer or referee).</summary>
    public Guid BeneficiaryMemberId { get; set; }

    /// <summary>referrer | referee</summary>
    public string BeneficiaryRole { get; set; } = "referrer";

    /// <summary>credit | free_days</summary>
    public string RewardType { get; set; } = "credit";

    /// <summary>EGP when credit; day count when free_days.</summary>
    public decimal RewardValue { get; set; }

    /// <summary>pending_hold | granted | reversed | forfeited</summary>
    public string Status { get; set; } = "pending_hold";

    public DateTime HoldUntilUtc { get; set; }
    public DateTime? GrantedAtUtc { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
    public DateTime? ForfeitedAtUtc { get; set; }

    /// <summary>MemberCredit row created on grant (credit rewards).</summary>
    public Guid? CreditEntryId { get; set; }

    /// <summary>Membership whose EndDate was extended (free_days).</summary>
    public Guid? ExtendedMembershipId { get; set; }

    public int? DaysGranted { get; set; }

    /// <summary>True when converting plan PlanType was family (INV-5 premium label).</summary>
    public bool IsFamily { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public MemberInvitation? Invitation { get; set; }
    public GymMember? BeneficiaryMember { get; set; }
}
