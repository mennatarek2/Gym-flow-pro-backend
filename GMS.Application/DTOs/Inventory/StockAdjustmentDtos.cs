namespace GMS.Application.DTOs.Inventory;

public class StockAdjustmentLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public string? ProductName { get; set; }
    public decimal QtyDelta { get; set; }
    public decimal? UnitCost { get; set; }
    public Guid? BatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateOnly? ExpiresOn { get; set; }
}

public class StockAdjustmentDto
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public string? WarehouseName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string? Note { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int LineCount { get; set; }
    /// <summary>Server Σ(QtyDelta × UnitCost). Valuation signal for managers; still returned for adjust callers.</summary>
    public decimal EstimatedValueImpactEgp { get; set; }
    public List<StockAdjustmentLineDto> Lines { get; set; } = new();
}

public class CreateStockAdjustmentLineRequest
{
    public Guid ProductId { get; set; }
    public decimal QtyDelta { get; set; }
    public decimal? UnitCost { get; set; }
    public Guid? BatchId { get; set; }
}

public class CreateStockAdjustmentRequest
{
    /// <summary>Optional — server auto-resolves to tenant default warehouse when null.</summary>
    public Guid? WarehouseId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? Note { get; set; }
    public List<CreateStockAdjustmentLineRequest> Lines { get; set; } = new();
}
