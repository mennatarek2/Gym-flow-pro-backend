namespace GMS.Application.DTOs.Inventory;

public class ProductCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class CreateProductCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateProductCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ProductDto
{
    public Guid Id { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public Guid? DefaultSupplierId { get; set; }
    public string? DefaultSupplierName { get; set; }
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
    /// <summary>Null when caller lacks manage/purchase/financial.view (C4).</summary>
    public decimal? CostPrice { get; set; }
    public string Currency { get; set; } = "EGP";
    public bool Taxable { get; set; }
    public decimal? VatRatePercent { get; set; }
    public bool TrackStock { get; set; }
    public bool TrackBatch { get; set; }
    public bool TrackExpiry { get; set; }
    public bool AllowFractionalQty { get; set; }
    public bool IsSellable { get; set; }
    public bool IsPurchasable { get; set; }
    public bool VisibleToMembers { get; set; }
    public decimal ReorderMinQty { get; set; }
    public bool IsActive { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class CreateProductRequest
{
    public Guid? CategoryId { get; set; }
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
    public bool VisibleToMembers { get; set; }
    public decimal ReorderMinQty { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateProductRequest
{
    public Guid? CategoryId { get; set; }
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
    public bool VisibleToMembers { get; set; }
    public decimal ReorderMinQty { get; set; }
    public bool IsActive { get; set; } = true;
}
