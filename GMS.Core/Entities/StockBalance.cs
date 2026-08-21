namespace GMS.Core.Entities;

/// <summary>
/// Cached on-hand qty per product/warehouse[/batch]. Written only by <c>IStockLedgerService</c>.
/// </summary>
public class StockBalance : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? BatchId { get; set; }

    public decimal QtyOnHand { get; set; }

    /// <summary>Optimistic concurrency token — prevents lost updates under concurrent ledger posts.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Tenant? Tenant { get; set; }
    public Product? Product { get; set; }
    public Warehouse? Warehouse { get; set; }
}
