namespace GMS.Core.Entities;

/// <summary>
/// Per-tenant, per-year, per-type running counter used to allocate gap-free sequential invoice
/// numbers. Deliberately NOT a BaseEntity — no soft delete, always queried by explicit TenantId.
/// </summary>
public class InvoiceSequence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public int Year { get; set; }

    /// <summary>'invoice' | 'credit_note'</summary>
    public string Type { get; set; } = string.Empty;

    public int LastNumber { get; set; }
}
