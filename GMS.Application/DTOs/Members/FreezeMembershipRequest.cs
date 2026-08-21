namespace GMS.Application.DTOs.Members;

/// <summary>
/// Request DTO for freezing a membership.
/// </summary>
public class FreezeMembershipRequest
{
    public DateTime FrozenUntil { get; set; }
    public string? Reason { get; set; }
}
