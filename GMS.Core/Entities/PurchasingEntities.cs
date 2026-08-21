namespace GMS.Core.Entities;

public class Supplier : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Notes { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; set; }
    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
}

public class PurchaseOrder : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    /// <summary>draft | approved | partially_received | received | cancelled</summary>
    public string Status { get; set; } = "draft";
    public DateTime OrderedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public string? Notes { get; set; }

    public Tenant? Tenant { get; set; }
    public Supplier? Supplier { get; set; }
    public Warehouse? Warehouse { get; set; }
    public AppUser? ApprovedByUser { get; set; }
    public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();
    public ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();
}

public class PurchaseOrderLine : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid ProductId { get; set; }
    public decimal QtyOrdered { get; set; }
    public decimal QtyReceived { get; set; }
    public decimal UnitCost { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }
    public Product? Product { get; set; }
}

public class GoodsReceipt : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public Guid ReceivedByUserId { get; set; }

    public Tenant? Tenant { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Warehouse? Warehouse { get; set; }
    public AppUser? ReceivedByUser { get; set; }
    public ICollection<GoodsReceiptLine> Lines { get; set; } = new List<GoodsReceiptLine>();
}

public class GoodsReceiptLine : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid GoodsReceiptId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitCost { get; set; }
    public string? BatchNumber { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public Guid? ProductBatchId { get; set; }

    public GoodsReceipt? GoodsReceipt { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
    public Product? Product { get; set; }
    public ProductBatch? ProductBatch { get; set; }
}

/// <summary>Product lot for batch/expiry tracking (INVS-5).</summary>
public class ProductBatch : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly? ExpiresOn { get; set; }

    public Tenant? Tenant { get; set; }
    public Product? Product { get; set; }
}

/// <summary>Lightweight supplier AP ledger (INVS-5). Positive Amount = amount owed to supplier.</summary>
public class SupplierLedgerEntry : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid SupplierId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>purchase | payment | return_credit | opening</summary>
    public string Reason { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Note { get; set; }

    public Tenant? Tenant { get; set; }
    public Supplier? Supplier { get; set; }
}
