namespace GMS.Core.Entities;

/// <summary>
/// A single signed cash movement within a <see cref="Shift"/>.
/// </summary>
public class CashMovement : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid ShiftId { get; set; }

    /// <summary>'sale' | 'refund' | 'paid_in' | 'paid_out' | 'float_adjust'</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Signed: positive = cash in, negative = cash out.</summary>
    public decimal Amount { get; set; }

    /// <summary>Polymorphic pointer (e.g. Sale.Id for type='sale') — no FK constraint.</summary>
    public Guid? ReferenceId { get; set; }

    public string? Reason { get; set; }

    /// <summary>app_users.Id — who recorded this movement.</summary>
    public Guid CreatedByUserId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Shift? Shift { get; set; }
    public AppUser? CreatedByUser { get; set; }
}
