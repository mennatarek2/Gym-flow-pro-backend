namespace GMS.Application.DTOs.Shifts;

public class CashMovementDto
{
    public Guid Id { get; set; }

    /// <summary>'sale' | 'refund' | 'paid_in' | 'paid_out' | 'float_adjust'</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Signed: positive = cash in, negative = cash out.</summary>
    public decimal Amount { get; set; }

    public Guid? ReferenceId { get; set; }
    public string? Reason { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
