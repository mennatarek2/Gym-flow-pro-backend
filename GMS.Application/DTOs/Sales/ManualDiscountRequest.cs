namespace GMS.Application.DTOs.Sales;

public class ManualDiscountRequest
{
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}
