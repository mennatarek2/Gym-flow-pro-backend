namespace GMS.Application.DTOs.Trials;

public class TrialInitiateRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? FullNameAr { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
}
