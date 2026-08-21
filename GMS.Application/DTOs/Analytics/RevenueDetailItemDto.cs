namespace GMS.Application.DTOs.Analytics;

/// <summary>
/// Detailed revenue transaction for reports.
/// </summary>
public class RevenueDetailItemDto
{
    public Guid Id { get; set; }
    public DateTime TransactionDate { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty; // cash, paymob, fawry
}
