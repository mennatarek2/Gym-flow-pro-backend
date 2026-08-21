namespace GMS.Application.DTOs.Invitation;

public class UpdateInvitationStatusRequest
{
    /// <summary>new | contacted | interested | not_interested | converted</summary>
    public string Status { get; set; } = string.Empty;
}
