namespace GMS.Application.DTOs.Sales;

/// <summary>One payment leg of a (possibly split) sale payment.</summary>
public class SalePaymentRequest
{
    /// <summary>cash | card_paymob | fawry | vodafone | instapay | account_credit</summary>
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
