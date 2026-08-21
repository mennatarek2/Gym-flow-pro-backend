namespace GMS.Application.DTOs.ZReports;

public class ZReportMethodTotalDto
{
    /// <summary>cash|card_paymob|fawry|vodafone|instapay|account_credit</summary>
    public string Method { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Total { get; set; }
}
