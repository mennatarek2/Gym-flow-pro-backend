namespace GMS.Application.DTOs.Expenses;

public sealed class CashExpenseDto
{
    public Guid Id { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public string? Payee { get; set; }
    public string? Description { get; set; }
    public string? SourceType { get; set; }
    public string? SourceReference { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid? ShiftId { get; set; }
    public Guid RecordedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class CreateCashExpenseRequest
{
    public DateOnly ExpenseDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public string? Payee { get; set; }
    public string? Description { get; set; }
    public string? SourceType { get; set; }
    public string? SourceReference { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid? ShiftId { get; set; }
}

public sealed class UpdateCashExpenseRequest
{
    public DateOnly? ExpenseDate { get; set; }
    public string? Category { get; set; }
    public decimal? Amount { get; set; }
    public string? Status { get; set; }
    public string? Note { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Payee { get; set; }
    public string? Description { get; set; }
    public string? SourceType { get; set; }
    public string? SourceReference { get; set; }
    public Guid? ShiftId { get; set; }
}
