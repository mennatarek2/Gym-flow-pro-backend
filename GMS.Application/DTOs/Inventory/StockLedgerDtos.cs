namespace GMS.Application.DTOs.Inventory;

public class StockLedgerPostRequest
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? BatchId { get; set; }
    public decimal QtyDelta { get; set; }
    public decimal? UnitCost { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Note { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

public class StockMovementDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? BatchId { get; set; }
    public decimal QtyDelta { get; set; }
    public decimal? UnitCost { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Note { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class StockOnHandDto
{
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? BatchId { get; set; }
    public decimal QtyOnHand { get; set; }
    /// <summary>Sellable qty at this warehouse (physical excluding expired batches).</summary>
    public decimal QtyAvailable { get; set; }
    public string? ProductSku { get; set; }
    public string? ProductName { get; set; }
    public string? WarehouseCode { get; set; }
    public string? WarehouseName { get; set; }
}

public class ProductStockBreakdownDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal TotalOnHand { get; set; }
    public decimal TotalAvailable { get; set; }
    public List<StockOnHandDto> Warehouses { get; set; } = new();
    /// <summary>G4 — per-batch buckets (qty &gt; 0) for Fix / write-off pickers.</summary>
    public List<StockBatchBucketDto> Batches { get; set; } = new();
}

public class StockBatchBucketDto
{
    public Guid WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public Guid? BatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public decimal QtyOnHand { get; set; }
    public bool IsExpired { get; set; }
}

public class StockQueryResponse
{
    public decimal QtyOnHand { get; set; }
    public decimal QtyAvailable { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public List<StockMovementDto>? Movements { get; set; }
}

public class StockBoardRowDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? ImageUrl { get; set; }
    public decimal ReorderMinQty { get; set; }
    /// <summary>Physical on-hand (all buckets).</summary>
    public decimal OnHand { get; set; }
    /// <summary>Sellable qty (excludes expired). Aligns with POS available check.</summary>
    public decimal Available { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
}

/// <summary>One FEFO allocation slice for a sale deduction.</summary>
public class StockAllocationSlice
{
    public Guid? BatchId { get; set; }
    public decimal Qty { get; set; }
}
