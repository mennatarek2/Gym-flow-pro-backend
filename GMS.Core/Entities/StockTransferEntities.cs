namespace GMS.Core.Entities;

/// <summary>INVS-8 warehouse-to-warehouse stock transfer.</summary>
public class StockTransfer : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }

    /// <summary>pending | in_transit | completed | cancelled</summary>
    public string Status { get; set; } = "pending";

    public string? Note { get; set; }

    public Guid CreatedByUserId { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? ReceivedByUserId { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public Warehouse? FromWarehouse { get; set; }
    public Warehouse? ToWarehouse { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? SubmittedByUser { get; set; }
    public AppUser? ReceivedByUser { get; set; }
    public AppUser? CancelledByUser { get; set; }
    public ICollection<StockTransferLine> Lines { get; set; } = new List<StockTransferLine>();
}

public class StockTransferLine : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid StockTransferId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Qty { get; set; }
    public Guid? BatchId { get; set; }

    public StockTransfer? StockTransfer { get; set; }
    public Product? Product { get; set; }
    public ProductBatch? Batch { get; set; }
}
