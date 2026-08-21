namespace GMS.Application.DTOs.CallSheet;

public class CallSheetEntryDto
{
    public Guid MembershipId { get; set; }
    public Guid MemberId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateOnly EndDate { get; set; }
    public DateTime? LastVisitAt { get; set; }

    /// <summary>'contacted' | 'renewed' | 'declined' | 'no_answer' — null if never called.</summary>
    public string? LastCallOutcome { get; set; }
}
