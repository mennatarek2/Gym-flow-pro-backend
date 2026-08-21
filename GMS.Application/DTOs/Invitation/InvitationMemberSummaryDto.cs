namespace GMS.Application.DTOs.Invitation;

/// <summary>Member 360 + Member App summary: quota + status counts + invited people.</summary>
public class InvitationMemberSummaryDto
{
    public InvitationQuotaDto Quota { get; set; } = new();
    public int Total { get; set; }
    public int New { get; set; }
    public int Contacted { get; set; }
    public int Interested { get; set; }
    public int NotInterested { get; set; }
    public int Converted { get; set; }
    public List<InvitationHistoryResponse> Items { get; set; } = new();
}
