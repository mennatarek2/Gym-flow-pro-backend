namespace GMS.Core.Entities;

/// <summary>INVS-4 stock adjustment header (opening / damage / etc.).</summary>
public class StockAdjustment : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid WarehouseId { get; set; }

    /// <summary>draft | posted | cancelled</summary>
    public string Status { get; set; } = "draft";

    /// <summary>opening | damage | lost | expired | manual_count | internal_use | employee | supplier_correction | other</summary>
    public string ReasonCode { get; set; } = string.Empty;

    public string? Note { get; set; }

    public Guid CreatedByUserId { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTime? PostedAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public Warehouse? Warehouse { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? PostedByUser { get; set; }
    public ICollection<StockAdjustmentLine> Lines { get; set; } = new List<StockAdjustmentLine>();
}
