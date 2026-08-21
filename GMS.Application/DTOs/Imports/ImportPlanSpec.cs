namespace GMS.Application.DTOs.Imports;

/// <summary>One plan to create from POST /api/imports/{id}/create-plans, for plan names the import
/// referenced but didn't match to an existing MembershipPlan.</summary>
public class ImportPlanSpec
{
    public string Name { get; set; } = string.Empty;

    /// <summary>'monthly_unlimited' | 'session_pack' | 'time_limited' | 'pt_credits' | 'family' | 'trial' | 'day_pass'</summary>
    public string PlanType { get; set; } = "monthly_unlimited";

    public int DurationDays { get; set; }
    public decimal Price { get; set; }
}
