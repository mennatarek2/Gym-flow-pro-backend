namespace GMS.Application.DTOs.Invoices;

/// <summary>Minimal payment info rendered as a "Payment Received" section on a receipt.</summary>
public class PaymentReceiptInfoDto
{
    public decimal Amount { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public string? Method { get; set; }
}
