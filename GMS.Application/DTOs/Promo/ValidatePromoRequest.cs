namespace GMS.Application.DTOs.Promo;

/// <summary>Request body for POST /api/sales/validate-promo.</summary>
public class ValidatePromoRequest
{
    public string Code { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public Guid MemberId { get; set; }
}
