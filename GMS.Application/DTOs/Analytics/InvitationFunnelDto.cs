namespace GMS.Application.DTOs.Analytics;

/// <summary>
/// Invitation funnel: overall totals (backward compatible) plus guest_pass vs referral breakdown
/// and Cairo-month contribution of referral conversions to new members.
/// </summary>
public class InvitationFunnelDto
{
    /// <summary>Invitation product totals (type = invitation).</summary>
    public int Sent { get; set; }
    public int New { get; set; }
    public int Contacted { get; set; }
    public int Interested { get; set; }
    public int NotInterested { get; set; }
    public int Visited { get; set; }
    public int Converted { get; set; }
    public decimal ConversionRate { get; set; }

    public InvitationTypeFunnelDto GuestPass { get; set; } = new();
    public InvitationTypeFunnelDto Referral { get; set; } = new();

    /// <summary>Cairo calendar month denominator for contribution %.</summary>
    public int NewMembersThisMonth { get; set; }

    /// <summary>Distinct members converted via referral this Cairo month.</summary>
    public int ReferralConvertedMembersThisMonth { get; set; }

    /// <summary>0–100: referral conversions / new members this month (0 if no new members).</summary>
    public decimal PercentNewMembersFromReferrals { get; set; }
}

/// <summary>Per–InvitationType funnel slice.</summary>
public class InvitationTypeFunnelDto
{
    public int Sent { get; set; }
    public int Visited { get; set; }
    public int Converted { get; set; }
    public decimal ConversionRate { get; set; }
}
