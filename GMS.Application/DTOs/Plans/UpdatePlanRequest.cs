namespace GMS.Application.DTOs.Plans;

using GMS.Application.DTOs.Activities;

/// <summary>
/// Request DTO for updating an existing membership plan.
/// </summary>
public class UpdatePlanRequest
{
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string PlanType { get; set; } = "monthly_unlimited";
    // Valid values: 'monthly_unlimited', 'session_pack', 'time_limited', 'pt_credits', 'family', 'trial', 'day_pass'

    public decimal Price { get; set; }
    public int DurationDays { get; set; }
    public int? SessionCount { get; set; }
    public TimeOnly? TimeRestrictionStart { get; set; }
    public TimeOnly? TimeRestrictionEnd { get; set; }
    public int InvitationQuota { get; set; } = 0;
    public int ReferralInviteQuota { get; set; } = 0;
    public string? ReferralRewardType { get; set; }
    public decimal? ReferralRewardValue { get; set; }
    public int? TrialVisitLimit { get; set; }

    /// <summary>Null = leave existing entitlements. Explicit list replaces them.</summary>
    public List<UpsertPlanEntitlementRequest>? Entitlements { get; set; }
}
