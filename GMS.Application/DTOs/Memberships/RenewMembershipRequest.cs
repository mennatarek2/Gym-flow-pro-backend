namespace GMS.Application.DTOs.Memberships;

/// <summary>
/// Request DTO for renewing a membership.
/// If PlanId is null, renews with the same plan.
/// </summary>
public class RenewMembershipRequest
{
    public Guid? PlanId { get; set; } // Optional: null = renew same plan
    public string PaymentMethod { get; set; } = "cash";
    // Valid values: 'cash', 'paymob', 'fawry', 'vodafone_cash'

    public decimal AmountPaid { get; set; }

    /// <summary>
    /// How to handle remaining days when the current membership still covers today.
    /// Valid: cancel_and_switch (default) | queue_next | manual_rollover.
    /// Ignored for dating when there is no covering membership.
    /// </summary>
    public string TransitionMode { get; set; } = "cancel_and_switch";
}
