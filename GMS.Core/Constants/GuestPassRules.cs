namespace GMS.Core.Constants;

/// <summary>Guest-pass operational guardrails (desk redeem / abuse caps).</summary>
public static class GuestPassRules
{
    /// <summary>Max redeemed guest_pass visits per phone per rolling window.</summary>
    public const int MaxVisitsPerPhone = 1;

    /// <summary>Rolling window for the per-phone visit cap.</summary>
    public const int VisitCapDays = 90;
}
