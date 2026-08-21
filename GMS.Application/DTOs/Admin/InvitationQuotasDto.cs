namespace GMS.Application.DTOs.Admin;

/// <summary>
/// DTO for invitation quota settings per plan type.
/// </summary>
public class InvitationQuotasDto
{
    public Dictionary<string, int> QuotasByPlanType { get; set; } = new();
    // Example: { "monthly_unlimited": 3, "session_pack": 2, "family": 5 }
}
