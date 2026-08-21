namespace GMS.Application.DTOs.Memberships;

/// <summary>
/// Historical membership record for history/audit trail.
/// </summary>
public class MembershipHistoryItemDto
{
    public Guid Id { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanNameAr { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime? PaymentDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
