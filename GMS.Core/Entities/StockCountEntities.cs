namespace GMS.Core.Entities;

/// <summary>INVS-9 periodic stock count with snapshot + approval.</summary>
public class StockCount : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid WarehouseId { get; set; }

    /// <summary>draft | submitted | approved | cancelled</summary>
    public string Status { get; set; } = "draft";

    public DateTime CountedAtUtc { get; set; }
    public string? Note { get; set; }

    public Guid CreatedByUserId { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public Warehouse? Warehouse { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? SubmittedByUser { get; set; }
    public AppUser? ApprovedByUser { get; set; }
    public ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
}

public class StockCountLine : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid StockCountId { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>On-hand snapshot frozen at count start.</summary>
    public decimal SystemQty { get; set; }

    public decimal CountedQty { get; set; }

    /// <summary>CountedQty - SystemQty (persisted for audit).</summary>
    public decimal Variance { get; set; }

    public StockCount? StockCount { get; set; }
    public Product? Product { get; set; }
}
