namespace GMS.Application.DTOs.Plans;

using GMS.Application.DTOs.Activities;

/// <summary>
/// Request DTO for creating a new membership plan.
/// </summary>
public class CreatePlanRequest
{
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string PlanType { get; set; } = "monthly_unlimited";
    // Valid values: 'monthly_unlimited', 'session_pack', 'time_limited', 'pt_credits', 'family', 'trial', 'day_pass'

    public decimal Price { get; set; }
    public int DurationDays { get; set; }
    public int? SessionCount { get; set; } // For session_pack plans (10, 20, or 50)
    public TimeOnly? TimeRestrictionStart { get; set; } // For time_limited plans
    public TimeOnly? TimeRestrictionEnd { get; set; } // For time_limited plans
    public int InvitationQuota { get; set; } = 0; // retired guest-pass column; unused by product
    public int ReferralInviteQuota { get; set; } = 0; // Invitations per covering membership
    /// <summary>credit | free_days — null = not configured</summary>
    public string? ReferralRewardType { get; set; }
    /// <summary>EGP when credit; day count when free_days</summary>
    public decimal? ReferralRewardValue { get; set; }
    public int? TrialVisitLimit { get; set; } // For trial plans with a visit cap instead of/alongside date expiry

    /// <summary>Null = default gym-floor included. Explicit list (including empty) is stored as-is.</summary>
    public List<UpsertPlanEntitlementRequest>? Entitlements { get; set; }
}
