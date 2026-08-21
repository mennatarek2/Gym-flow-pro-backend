namespace GMS.Application.DTOs.Members;

/// <summary>
/// Lightweight DTO for member list/search endpoints.
/// </summary>
public class MemberListItemDto
{
    public Guid Id { get; set; }
    public string MemberNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string FullNameAr { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? ActivePlan { get; set; }
    public string? ActivePlanAr { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? MembershipStatus { get; set; }
    public string? PlanType { get; set; }
    public int? SessionsRemaining { get; set; }
}
