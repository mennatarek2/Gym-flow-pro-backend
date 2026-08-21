namespace GMS.Core.Entities;

/// <summary>
/// Represents a gym member/client.
/// Multi-tenant: tenant_id is required and indexed.
/// </summary>
public class GymMember : BaseEntity
{
    // Tenant context
    public Guid TenantId { get; set; }

    // Member identification
    public string MemberNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string FullNameAr { get; set; } = string.Empty;

    // Contact information
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Security & Identity
    public string NationalIdEncrypted { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }

    // Profile
    public string ProfilePhotoUrl { get; set; } = string.Empty;
    public Guid? AppUserId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public bool PaperWaiverOnFile { get; set; } = false;

    // Trial tracking
    public bool IsTrial { get; set; } = false;

    /// <summary>'active_trial' | 'converted' | 'expired' — null if this member never had a trial.</summary>
    public string? TrialOutcome { get; set; }
    public DateTime? TrialConvertedAt { get; set; }

    /// <summary>The Sale that converted this trial into a paid membership.</summary>
    public Guid? ConvertingSaleId { get; set; }

    // Invitation tracking
    public int InvitationQuotaRemaining { get; set; } = 0;
    public DateTime? LastInvitationResetDate { get; set; }

    /// <summary>Shareable referral code, unique per tenant when set.</summary>
    public string? ReferralCode { get; set; }

    /// <summary>Successful referral conversions that completed reward grant.</summary>
    public int SuccessfulReferralCount { get; set; }

    /// <summary>null | ambassador (and future light tiers — not a points ledger).</summary>
    public string? ReferralTier { get; set; }

    // Timestamps
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public AppUser? AppUser { get; set; }
    public Sale? ConvertingSale { get; set; }
    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<GymAttendance> Attendances { get; set; } = new List<GymAttendance>();
    public ICollection<MemberInvitation> InvitationsSent { get; set; } = new List<MemberInvitation>();
    public ICollection<MemberInvitation> InvitationsConverted { get; set; } = new List<MemberInvitation>();
}
