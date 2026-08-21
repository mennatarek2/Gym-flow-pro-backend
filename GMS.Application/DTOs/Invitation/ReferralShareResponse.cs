namespace GMS.Application.DTOs.Invitation;

/// <summary>Member-facing referral share payload + family rules copy.</summary>
public class ReferralShareResponse
{
    public string ReferralCode { get; set; } = string.Empty;
    public string ShareText { get; set; } = string.Empty;
    public string ShareTextAr { get; set; } = string.Empty;
    /// <summary>Placeholder deep-link with <c>?ref=CODE</c> — public join may land later.</summary>
    public string ShareUrlHint { get; set; } = string.Empty;
    public int SuccessfulReferralCount { get; set; }
    public string ReferralTier { get; set; } = "none";

    /// <summary>Tenant family premium multiplier (default 1.5).</summary>
    public decimal FamilyRewardMultiplier { get; set; } = 1.5m;

    public string FamilyLabelEn { get; set; } = "Family";
    public string FamilyLabelAr { get; set; } = "عائلة";
    public string FamilyRulesEn { get; set; } = string.Empty;
    public string FamilyRulesAr { get; set; } = string.Empty;
}
