namespace GMS.Application.DTOs.Shifts;

public class ShiftDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? UserName { get; set; }

    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public decimal OpeningFloat { get; set; }

    /// <summary>
    /// Null while the shift is open — a blind count: the counting staff member must never see the
    /// system-expected total before entering their physical count. Populated only once closed.
    /// </summary>
    public decimal? ExpectedCash { get; set; }

    public decimal? CountedCash { get; set; }
    public decimal? Variance { get; set; }
    public string? VarianceNote { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>'open' | 'closed' | 'approved'</summary>
    public string Status { get; set; } = string.Empty;

    public List<CashMovementDto> Movements { get; set; } = new();
}
