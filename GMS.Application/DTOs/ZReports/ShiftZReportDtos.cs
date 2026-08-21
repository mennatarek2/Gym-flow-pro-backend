namespace GMS.Application.DTOs.ZReports;

/// <summary>Shift closing Z-Report — what happened on this drawer, and can cash be reconciled?</summary>
public class ShiftZReportDto
{
    public Guid ShiftId { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public string GymName { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    /// <summary>open | closed | approved</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>True when the drawer is closed or approved — cash figures are frozen on the Shift row.</summary>
    public bool IsFinal { get; set; }
    /// <summary>False while open (blind count). Expected / counted / difference stay null.</summary>
    public bool RevealCash { get; set; }

    public decimal GrossSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal Refunds { get; set; }
    public decimal NetSales { get; set; }
    public int TransactionCount { get; set; }

    public List<ZReportMethodTotalDto> Methods { get; set; } = new();

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

public class ShiftZReportListDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<ShiftZReportListItemDto> Items { get; set; } = new();
}

public class ShiftZReportListItemDto
{
    public Guid ShiftId { get; set; }
    public Guid UserId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public decimal Refunds { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Difference { get; set; }
}
