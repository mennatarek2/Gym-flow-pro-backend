namespace GMS.Application.DTOs.Expenses;

public sealed class CashExpenseDto
{
    public Guid Id { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public Guid RecordedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class CreateCashExpenseRequest
{
    public DateOnly ExpenseDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

public sealed class UpdateCashExpenseRequest
{
    public DateOnly? ExpenseDate { get; set; }
    public string? Category { get; set; }
    public decimal? Amount { get; set; }
    public string? Status { get; set; }
    public string? Note { get; set; }
}
