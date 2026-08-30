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
    public Guid RecordedByUserId { get; set; }

    public Tenant? Tenant { get; set; }
    public AppUser? RecordedByUser { get; set; }
}
