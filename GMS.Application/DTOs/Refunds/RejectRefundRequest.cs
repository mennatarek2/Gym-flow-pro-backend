namespace GMS.Application.DTOs.Refunds;

/// <summary>Request body for POST /api/refunds/{id}/reject.</summary>
public class RejectRefundRequest
{
    public string Note { get; set; } = string.Empty;
}
