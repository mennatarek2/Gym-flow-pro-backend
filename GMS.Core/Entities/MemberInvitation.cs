namespace GMS.Core.Entities;

/// <summary>
/// Invitation: a member submits a friend's contact so staff can follow up.
/// Table remains member_invitations — do not invent a Lead table.
/// Historical rows may still be guest_pass / referral.
/// </summary>
public class MemberInvitation : BaseEntity
{
    public Guid TenantId { get; set; }

    public Guid InvitingMemberId { get; set; }
    public Guid? ConvertedMemberId { get; set; }

    /// <summary>Covering membership that spent 1 quota on create. Null on historical guest/referral rows.</summary>
    public Guid? CoveringMembershipId { get; set; }

    /// <summary>invitation (product) | guest_pass | referral (historical)</summary>
    public string InvitationType { get; set; } = "invitation";

    public string GuestName { get; set; } = string.Empty;
    public string GuestPhoneNumber { get; set; } = string.Empty;

    /// <summary>Optional. Encrypted with the same AES path as GymMember.NationalIdEncrypted.</summary>
    public string? NationalIdEncrypted { get; set; }

    public string? Notes { get; set; }

    /// <summary>Guest pass planned visit; unused by the Invitation product.</summary>
    public DateOnly? VisitDate { get; set; }

    /// <summary>new | contacted | interested | not_interested | converted (product). Historical: pending/visited/expired.</summary>
    public string Status { get; set; } = "new";

    /// <summary>Snapshot of inviter share code on historical referral rows.</summary>
    public string? ReferralCodeUsed { get; set; }

    /// <summary>Sale that converted the invited person (attribution).</summary>
    public Guid? ConvertingSaleId { get; set; }

    /// <summary>YYYY-MM guest_pass quota bucket only. Unused by Invitation product.</summary>
    public string QuotaPeriod { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; }
    public DateTime? VisitedAtUtc { get; set; }
    public DateTime? ContactedAtUtc { get; set; }
    public DateTime? ConvertedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public GymMember? InvitingMember { get; set; }
    public GymMember? ConvertedMember { get; set; }
}
