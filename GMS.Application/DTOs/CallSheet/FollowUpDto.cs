namespace GMS.Application.DTOs.CallSheet;

public class FollowUpDto
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public Guid? MembershipId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string MemberNumber { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? ProfilePhotoUrl { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime DueAtUtc { get; set; }
    public string? NextAction { get; set; }
    public DateTime? NextActionAtUtc { get; set; }
    public string? Why { get; set; }
    public string? RelatedType { get; set; }
    public Guid? RelatedId { get; set; }
    public DateTime? LastContactAtUtc { get; set; }
    public string? LastOutcome { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public class FollowUpDetailDto : FollowUpDto
{
    public string? Notes { get; set; }
    public List<FollowUpHistoryDto> History { get; set; } = new();
}

public class FollowUpHistoryDto
{
    public Guid Id { get; set; }
    public DateTime AtUtc { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? NextAction { get; set; }
    public DateTime? NextActionAtUtc { get; set; }
    public string? StaffName { get; set; }
}

public class FollowUpSummaryDto
{
    public int ToCallToday { get; set; }
    public int HighPriority { get; set; }
    public int Pending { get; set; }
    public int ContactedToday { get; set; }
    public int NoAnswerToday { get; set; }
    public int Overdue { get; set; }
}

public class FollowUpListDto
{
    public FollowUpSummaryDto Summary { get; set; } = new();
    public List<FollowUpDto> Items { get; set; } = new();
}

public class CreateFollowUpRequest
{
    public Guid MemberId { get; set; }
    public string Reason { get; set; } = "custom";
    public string Priority { get; set; } = "medium";
    public DateTime? DueAtUtc { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string? Notes { get; set; }
    public string? Why { get; set; }
    public Guid? MembershipId { get; set; }
    public string? RelatedType { get; set; }
    public Guid? RelatedId { get; set; }
}

public class CompleteFollowUpRequest
{
    public string? Note { get; set; }
}
