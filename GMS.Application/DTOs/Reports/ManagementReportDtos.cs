namespace GMS.Application.DTOs.Reports;

public class SalesReportDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal CashInTotal { get; set; }
    public decimal CashRefundsTotal { get; set; }
    public decimal NetCashIn { get; set; }
    public decimal BookedTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal MembershipCashIn { get; set; }
    public decimal ProductCashIn { get; set; }
    public decimal MixedCashIn { get; set; }
    public int TransactionCount { get; set; }
    public bool PaymentsTruncated { get; set; }
    public List<ReportMethodTotalDto> Methods { get; set; } = new();
    public List<ReportLineTypeTotalDto> LineTypes { get; set; } = new();
    public List<SalesReportDayDto> Days { get; set; } = new();
    public List<SalesReportStaffOptionDto> Staff { get; set; } = new();
    public List<string> MethodOptions { get; set; } = new();
    public List<SalesReportPaymentRowDto> Payments { get; set; } = new();
}

public class SalesReportDayDto
{
    public DateOnly Date { get; set; }
    public decimal CashIn { get; set; }
}

public class SalesReportStaffOptionDto
{
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ReportMethodTotalDto
{
    public string Method { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal CashIn { get; set; }
}

public class ReportLineTypeTotalDto
{
    public string LineType { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Booked { get; set; }
}

public class SalesReportPaymentRowDto
{
    public Guid Id { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid? SaleId { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public Guid? StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    /// <summary>membership | product | mixed | other | unknown — from SaleLine.LineType on the sale, not a split of the payment.</summary>
    public string Type { get; set; } = "unknown";
}

public class RefundsReportDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal Total { get; set; }
    public decimal CashTotal { get; set; }
    public decimal CreditTotal { get; set; }
    public decimal GatewayTotal { get; set; }
    public int Count { get; set; }
    public int SaleCount { get; set; }
    public decimal Average { get; set; }
    public bool Truncated { get; set; }
    public List<SalesReportStaffOptionDto> Staff { get; set; } = new();
    public List<string> MethodOptions { get; set; } = new();
    public List<RefundsReportRowDto> Items { get; set; } = new();
}

public class RefundsReportRowDto
{
    public Guid Id { get; set; }
    public DateTime ExecutedAtUtc { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid SaleId { get; set; }
    public Guid? MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public Guid? StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public Guid? OriginalInvoiceId { get; set; }
    public string? OriginalInvoiceNumber { get; set; }
    public Guid? CreditNoteId { get; set; }
    public string? CreditNoteNumber { get; set; }
}

public class MembershipsReportDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int Started { get; set; }
    public int NewCount { get; set; }
    public int RenewalCount { get; set; }
    public decimal Revenue { get; set; }
    public int RefundedCount { get; set; }
    public int Cancelled { get; set; }
    public int Expired { get; set; }
    public bool Truncated { get; set; }
    public List<SalesReportStaffOptionDto> Staff { get; set; } = new();
    public List<MembershipsReportPlanOptionDto> Plans { get; set; } = new();
    public List<MembershipsReportPlanBreakdownDto> ByPlan { get; set; } = new();
    public List<MembershipsReportRowDto> StartedRows { get; set; } = new();
}

public class MembershipsReportPlanOptionDto
{
    public Guid PlanId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class MembershipsReportPlanBreakdownDto
{
    public Guid PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public int NewCount { get; set; }
    public int RenewalCount { get; set; }
    public decimal Revenue { get; set; }
}

public class MembershipsReportRowDto
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    /// <summary>new | renewal — renewal when LastRenewalDate or PlanTransitionMode is set.</summary>
    public string Type { get; set; } = "new";
    public Guid? StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Effective membership status, or refunded when an executed refund exists. Not GymMember.IsActive.</summary>
    public string Status { get; set; } = string.Empty;
    public bool Refunded { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public class ProductsReportDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal Revenue { get; set; }
    public int UnitsSold { get; set; }
    public int TransactionCount { get; set; }
    public Guid? TopProductId { get; set; }
    public string? TopProductName { get; set; }
    public bool Truncated { get; set; }
    public List<SalesReportStaffOptionDto> Staff { get; set; } = new();
    public List<ProductsReportProductOptionDto> Products { get; set; } = new();
    public List<string> MethodOptions { get; set; } = new();
    public List<ProductsReportRankedDto> TopProducts { get; set; } = new();
    public List<ProductsReportLineDto> Lines { get; set; } = new();
}

public class ProductsReportProductOptionDto
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ProductsReportRankedDto
{
    public Guid? ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
}

public class ProductsReportLineDto
{
    public Guid SaleLineId { get; set; }
    public DateTime SoldAtUtc { get; set; }
    public Guid SaleId { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public Guid? StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public string Payment { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
}

public class StaffShiftsReportDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal Sales { get; set; }
    public int TransactionCount { get; set; }
    public decimal Refunds { get; set; }
    public int ShiftCount { get; set; }
    public bool Truncated { get; set; }
    public List<SalesReportStaffOptionDto> StaffOptions { get; set; } = new();
    public List<StaffShiftOptionDto> ShiftOptions { get; set; } = new();
    public List<StaffCashInRowDto> StaffCashIn { get; set; } = new();
    public List<StaffShiftRowDto> Shifts { get; set; } = new();
    public List<StaffReportTxDto> Transactions { get; set; } = new();
}

public class StaffShiftOptionDto
{
    public Guid ShiftId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class StaffCashInRowDto
{
    public Guid? UserId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public int PaymentCount { get; set; }
    public decimal CashIn { get; set; }
    public decimal Refunds { get; set; }
    public int ShiftCount { get; set; }
}

public class StaffShiftRowDto
{
    public Guid ShiftId { get; set; }
    public Guid UserId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public decimal Refunds { get; set; }
    public decimal? OpeningFloat { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Variance { get; set; }
}

public class StaffReportTxDto
{
    /// <summary>sale | refund</summary>
    public string Type { get; set; } = "sale";
    public DateTime AtUtc { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public Guid? StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
}
