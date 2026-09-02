namespace GMS.Core.Entities;

/// <summary>
/// A recorded cash expense used by the dashboard's cash-basis profit figures.
/// Only posted, non-deleted rows are included in profit calculations.
/// </summary>
public sealed class CashExpense : BaseEntity
{
    public Guid TenantId { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = "posted";
    public string? Note { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public string? Payee { get; set; }
    public string? Description { get; set; }
    public string? SourceType { get; set; }
    public string? SourceReference { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid? ShiftId { get; set; }
    public Guid RecordedByUserId { get; set; }

    public Tenant? Tenant { get; set; }
    public AppUser? RecordedByUser { get; set; }
    public Shift? Shift { get; set; }
}
