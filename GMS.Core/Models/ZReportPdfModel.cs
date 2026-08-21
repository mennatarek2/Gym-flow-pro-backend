namespace GMS.Core.Models;

/// <summary>
/// Everything IZReportPdfRenderer needs to render one Z-Report PDF — a flat snapshot, not an EF
/// entity, so the renderer has no persistence dependency. Mirrors the aggregation captured in
/// ZReport.PayloadJson plus the tenant header fields the renderer needs.
/// </summary>
public class ZReportPdfModel
{
    public DateOnly ReportDate { get; set; }
    public DateTime GeneratedAt { get; set; }

    public string TenantName { get; set; } = string.Empty;
    public string TenantNameAr { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;

    public List<ZReportPdfMethodTotal> MethodTotals { get; set; } = new();
    public List<ZReportPdfLineTypeTotal> LineTypeTotals { get; set; } = new();

    public decimal PromoDiscountTotal { get; set; }
    public decimal ManualDiscountTotal { get; set; }
    public int ManualDiscountCount { get; set; }

    public decimal RefundsTotal { get; set; }

    public List<ZReportPdfShiftRow> Shifts { get; set; } = new();

    public decimal OutstandingAddedToday { get; set; }
    public decimal MembershipRevenueToday { get; set; }

    public string Currency { get; set; } = "EGP";
}

public class ZReportPdfMethodTotal
{
    public string Method { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Total { get; set; }
}

public class ZReportPdfLineTypeTotal
{
    public string LineType { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Revenue { get; set; }
}

public class ZReportPdfShiftRow
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
