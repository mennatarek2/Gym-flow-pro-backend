namespace GMS.Application.DTOs.ZReports;

public class ZReportDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateOnly ReportDate { get; set; }
    public string? PdfUrl { get; set; }
    public DateTime GeneratedAt { get; set; }
    public Guid? GeneratedByUserId { get; set; }

    public List<ZReportMethodTotalDto> MethodTotals { get; set; } = new();
    public List<ZReportLineTypeTotalDto> LineTypeTotals { get; set; } = new();

    public decimal PromoDiscountTotal { get; set; }
    public decimal ManualDiscountTotal { get; set; }
    public int ManualDiscountCount { get; set; }

    /// <summary>Always 0 until P11 adds the refunds table.</summary>
    public decimal RefundsTotal { get; set; }

    public List<ZReportShiftRowDto> Shifts { get; set; } = new();

    public decimal OutstandingAddedToday { get; set; }
    public decimal MembershipRevenueToday { get; set; }
}
