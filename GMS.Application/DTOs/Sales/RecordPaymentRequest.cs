namespace GMS.Application.DTOs.Sales;

/// <summary>Request body for POST /api/sales/{id}/payments.</summary>
public class RecordPaymentRequest
{
    /// <summary>cash | card_paymob | fawry | vodafone | instapay | account_credit</summary>
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
