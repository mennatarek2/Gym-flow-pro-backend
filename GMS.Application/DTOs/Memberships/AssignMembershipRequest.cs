namespace GMS.Application.DTOs.Memberships;

/// <summary>
/// Request DTO for assigning a membership to a member.
/// MemberId is provided in the URL path, not in the request body.
/// StartDate is automatically calculated from today.
/// </summary>
public class AssignMembershipRequest
{
    /// <summary>
    /// The membership plan to assign.
    /// </summary>
    public Guid PlanId { get; set; }

    /// <summary>
    /// Payment method for this membership.
    /// Valid values: 'cash', 'paymob', 'fawry'
    /// - 'cash': Membership becomes active immediately
    /// - 'paymob'/'fawry': Membership is pending until payment is confirmed via webhook
    /// </summary>
    public string PaymentMethod { get; set; } = "cash";

    /// <summary>
    /// Cash taken now. Omit to charge the full plan price (previous assign behavior).
    /// Paying less than plan price leaves Sale.AmountDue — Collect Payment on Member 360.
    /// Ignored for gateway methods (pending, AmountPaid 0 until webhook).
    /// </summary>
    public decimal? AmountPaid { get; set; }

    /// <summary>Optional referral attribution (pending until paid activate).</summary>
    public string? ReferralCode { get; set; }

    /// <summary>Optional referring GymMember.Id.</summary>
    public Guid? ReferringMemberId { get; set; }
}
