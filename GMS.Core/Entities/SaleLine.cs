namespace GMS.Core.Entities;

/// <summary>
/// A single line item within a <see cref="Sale"/>.
/// </summary>
public class SaleLine : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid SaleId { get; set; }

    /// <summary>'membership' | 'trial' | 'day_pass' | 'retail' | 'fee'</summary>
    public string LineType { get; set; } = string.Empty;

    /// <summary>Polymorphic pointer (e.g. plan id, product id) depending on <see cref="LineType"/> — no FK constraint.</summary>
    public Guid? ReferenceId { get; set; }

    public string Description { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }

    public int Qty { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Sale? Sale { get; set; }
}
