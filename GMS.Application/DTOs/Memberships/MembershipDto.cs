namespace GMS.Application.DTOs.Memberships;

/// <summary>
/// Current membership information with sessions remaining and payment details.
/// </summary>
public class MembershipDto
{
    public Guid Id { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanNameAr { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? SessionsRemaining { get; set; }
    public decimal AmountPaid { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime? PaymentDate { get; set; }
    public bool AutoRenew { get; set; }
    public DateOnly? FrozenFromDate { get; set; }
    public DateOnly? FrozenUntilDate { get; set; }

    /// <summary>Plan session_pack total (when applicable).</summary>
    public int? SessionCount { get; set; }

    /// <summary>Classes &amp; activities entitlements for this membership's plan.</summary>
    public List<GMS.Application.DTOs.Members.MemberActivityQuotaDto> ActivityQuotas { get; set; } = new();

    /// <summary>Days until EndDate using Cairo business calendar (may be negative if expired).</summary>
    public int DaysRemaining =>
        EndDate.DayNumber - GMS.Core.Utilities.MembershipOperational.TodayCairo().DayNumber;
}
