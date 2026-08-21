namespace GMS.Application.DTOs.Refunds;

public class MemberCreditEntryDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }

    /// <summary>'refund' | 'payment_use' | 'adjustment'</summary>
    public string EntryType { get; set; } = string.Empty;

    public Guid? ReferenceId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
