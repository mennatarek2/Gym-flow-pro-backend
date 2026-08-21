namespace GMS.Application.DTOs.Shifts;

public class RecordMovementRequest
{
    /// <summary>'paid_in' | 'paid_out' | 'float_adjust' (POST-driven; 'sale'/'refund' are recorded internally)</summary>
    public string Type { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Reason { get; set; }
}
