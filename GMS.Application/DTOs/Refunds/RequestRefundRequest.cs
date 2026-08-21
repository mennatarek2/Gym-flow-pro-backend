namespace GMS.Application.DTOs.Refunds;

/// <summary>Request body for POST /api/refunds.</summary>
public class RequestRefundRequest
{
    public Guid SaleId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>'cash' | 'gateway' | 'credit'</summary>
    public string Method { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}
