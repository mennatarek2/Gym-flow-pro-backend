namespace GMS.Application.DTOs.ZReports;

public class ZReportShiftRowDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningFloat { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Variance { get; set; }

    /// <summary>'open' | 'closed' | 'approved'</summary>
    public string Status { get; set; } = string.Empty;
}
