namespace GMS.Application.DTOs.Sales;

public sealed class CreateSaleAdjustmentRequest
{
    public Guid SaleId { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = "write_off";
    public string Reason { get; set; } = string.Empty;
}

public sealed class SaleAdjustmentDto
{
    public Guid Id { get; init; }
    public Guid SaleId { get; init; }
    public decimal Amount { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public Guid CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class SaleBalanceReconciliationDto
{
    public Guid SaleId { get; init; }
    public decimal PreviousAmountDue { get; init; }
    public decimal CanonicalAmountDue { get; init; }
    public decimal AllocatedPayments { get; init; }
    public decimal PostedAdjustments { get; init; }
    public string Status { get; init; } = string.Empty;
}
