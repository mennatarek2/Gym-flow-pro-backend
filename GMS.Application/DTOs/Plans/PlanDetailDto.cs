namespace GMS.Application.DTOs.Plans;

using GMS.Application.DTOs.Activities;

/// <summary>
/// Full plan detail DTO with membership count and all configuration details.
/// </summary>
public class PlanDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DescriptionAr { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EGP";
    public int DurationDays { get; set; }
    public int? SessionCount { get; set; }
    public TimeOnly? TimeRestrictionStart { get; set; }
    public TimeOnly? TimeRestrictionEnd { get; set; }
    public int InvitationQuota { get; set; }
    public int ReferralInviteQuota { get; set; }
    /// <summary>credit | free_days</summary>
    public string? ReferralRewardType { get; set; }
    public decimal? ReferralRewardValue { get; set; }
    public int? TrialVisitLimit { get; set; }
    public bool IsActive { get; set; }
    public int ActiveMemberships { get; set; }
    public int TotalMemberships { get; set; }
    public List<PlanEntitlementDto> Entitlements { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
