namespace GMS.Core.Entities;

/// <summary>
/// A cash-drawer shift: opened with a starting float, accumulates <see cref="CashMovement"/> rows,
/// and closed with a physical cash count reconciled against the expected total.
/// </summary>
public class Shift : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>app_users.Id — the staff member who opened this shift.</summary>
    public Guid UserId { get; set; }

    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public decimal OpeningFloat { get; set; }

    /// <summary>OpeningFloat + SUM(cash_movements.Amount). Null until closed — never revealed to the
    /// counting staff member while the shift is still open (blind count).</summary>
    public decimal? ExpectedCash { get; set; }

    public decimal? CountedCash { get; set; }
    public decimal? Variance { get; set; }
    public string? VarianceNote { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    /// <summary>'open' | 'closed' | 'approved'</summary>
    public string Status { get; set; } = "open";

    // Navigation
    public Tenant? Tenant { get; set; }
    public AppUser? User { get; set; }
    public AppUser? ApprovedByUser { get; set; }
    public ICollection<CashMovement> Movements { get; set; } = new List<CashMovement>();
}
