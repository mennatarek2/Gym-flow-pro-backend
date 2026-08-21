namespace GMS.Core.Models;

/// <summary>Flat snapshot for the per-shift closing Z-Report PDF. Not an EF entity.</summary>
public class ShiftZReportPdfModel
{
    public string GymName { get; set; } = string.Empty;
    public string GymNameAr { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string Currency { get; set; } = "EGP";

    public Guid ShiftId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsFinal { get; set; }
    public bool RevealCash { get; set; }

    public decimal GrossSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal Refunds { get; set; }
    public decimal NetSales { get; set; }
    public int TransactionCount { get; set; }

    public List<ZReportPdfMethodTotal> Methods { get; set; } = new();

    public decimal OpeningCash { get; set; }
    public decimal CashSales { get; set; }
    public decimal CashRefunds { get; set; }
    public decimal CashExpenses { get; set; }
    public decimal CashPaidIn { get; set; }
    public decimal FloatAdjust { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Difference { get; set; }

    public decimal Memberships { get; set; }
    public int MembershipCount { get; set; }
    public decimal Renewals { get; set; }
    public int RenewalCount { get; set; }
    public decimal Products { get; set; }
    public int ProductCount { get; set; }
    public decimal Other { get; set; }
    public int OtherCount { get; set; }

    public int RefundCount { get; set; }
    public int DiscountCount { get; set; }
}
