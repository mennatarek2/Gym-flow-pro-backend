namespace GMS.Application.DTOs.Debtors;

public class DebtorDto
{
    public Guid? MemberId { get; set; }
    public string? GuestName { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public decimal TotalDue { get; set; }
    public DateOnly OldestDueDate { get; set; }

    /// <summary>'0-7' | '8-30' | '30+' — days elapsed since OldestDueDate.</summary>
    public string AgingBucket { get; set; } = string.Empty;

    public DateTime? LastPaymentAt { get; set; }
}
