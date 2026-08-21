namespace GMS.Core.Entities;

/// <summary>
/// Sellable / stockable catalog product (INVS-1). On-hand qty lives in the stock ledger (INVS-3), not here.
/// POS identity is <see cref="BaseEntity.Id"/> → <c>SaleLine.ReferenceId</c> when LineType = retail.
/// </summary>
public class Product : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid? CategoryId { get; set; }
    /// <summary>
    /// Optional supplier this gym normally buys the product from. Convenience only — not exclusive.
    /// </summary>
    public Guid? DefaultSupplierId { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string? Brand { get; set; }
    public string? ImageUrl { get; set; }
    public string UnitOfMeasure { get; set; } = "pcs";

    public decimal SellPrice { get; set; }
    public decimal CostPrice { get; set; }
    public string Currency { get; set; } = "EGP";
    public bool Taxable { get; set; } = true;
    public decimal? VatRatePercent { get; set; }

    public bool TrackStock { get; set; } = true;
    public bool TrackBatch { get; set; }
    public bool TrackExpiry { get; set; }
    public bool AllowFractionalQty { get; set; }
    public bool IsSellable { get; set; } = true;
    public bool IsPurchasable { get; set; } = true;
    /// <summary>When true, product appears in Member App store catalog (Stage 0).</summary>
    public bool VisibleToMembers { get; set; }
    public decimal ReorderMinQty { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsArchived { get; set; }

    public Tenant? Tenant { get; set; }
    public ProductCategory? Category { get; set; }
    public Supplier? DefaultSupplier { get; set; }
}
