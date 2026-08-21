namespace GMS.Application.DTOs.Trials;

using GMS.Application.DTOs.Members;

public class TrialConfirmResponse
{
    public MemberDetailDto Member { get; set; } = null!;
    public MembershipSummaryDto Membership { get; set; } = null!;
}
