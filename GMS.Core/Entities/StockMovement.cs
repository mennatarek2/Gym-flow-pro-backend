namespace GMS.Core.Entities;

/// <summary>
/// Append-only stock ledger row (INVS-3). On-hand = SUM(QtyDelta). Prefer compensating posts over soft-delete.
/// </summary>
public class StockMovement : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    /// <summary>Null until INVS-5 batches; balances use a filtered unique for null batch.</summary>
    public Guid? BatchId { get; set; }

    public decimal QtyDelta { get; set; }
    public decimal? UnitCost { get; set; }

    /// <summary>See <see cref="GMS.Core.Constants.StockMovementReasons"/>.</summary>
    public string Reason { get; set; } = string.Empty;

    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Note { get; set; }

    public DateTime OccurredAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Tenant? Tenant { get; set; }
    public Product? Product { get; set; }
    public Warehouse? Warehouse { get; set; }
}
