namespace GMS.Core.Entities;

/// <summary>Line on a stock adjustment. Ledger posts reference this row Id.</summary>
public class StockAdjustmentLine : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid StockAdjustmentId { get; set; }
    public Guid ProductId { get; set; }
    public decimal QtyDelta { get; set; }
    public decimal? UnitCost { get; set; }
    /// <summary>G4 — optional/required batch for integrity (esp. expired write-off).</summary>
    public Guid? BatchId { get; set; }

    public StockAdjustment? StockAdjustment { get; set; }
    public Product? Product { get; set; }
    public ProductBatch? Batch { get; set; }
}
