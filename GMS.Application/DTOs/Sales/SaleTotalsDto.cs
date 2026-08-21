namespace GMS.Application.DTOs.Sales;

public class SaleTotalsDto
{
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal AmountDue { get; set; }
}
