namespace GMS.Application.DTOs.ZReports;

public class ZReportLineTypeTotalDto
{
    /// <summary>'membership' | 'trial' | 'day_pass' | 'retail' | 'fee'</summary>
    public string LineType { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Revenue { get; set; }
}
