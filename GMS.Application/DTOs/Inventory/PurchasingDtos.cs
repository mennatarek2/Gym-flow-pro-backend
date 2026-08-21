namespace GMS.Application.DTOs.Inventory;

public class SupplierDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Notes { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Null when caller lacks manage/purchase/reports.financial.view.</summary>
    public decimal? PurchasesTotal { get; set; }
    /// <summary>Sum of payment amounts as positive (money paid). Null when redacted.</summary>
    public decimal? PaidTotal { get; set; }
    /// <summary>Σ ledger amounts (owed to supplier). Null when redacted.</summary>
    public decimal? DueTotal { get; set; }
}

public class CreateSupplierRequest
{
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Notes { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Optional opening absolute amount. Sign via <see cref="OpeningOwedToSupplier"/>.</summary>
    public decimal? OpeningAmount { get; set; }
    /// <summary>True = له (owed to supplier / +); false = عليه (−). Ignored when OpeningAmount null/0.</summary>
    public bool? OpeningOwedToSupplier { get; set; }
}

public class UpdateSupplierRequest : CreateSupplierRequest { }

public class SupplierBalanceDto
{
    public Guid SupplierId { get; set; }
    public decimal PurchasesTotal { get; set; }
    public decimal PaidTotal { get; set; }
    public decimal OpeningTotal { get; set; }
    public decimal DueTotal { get; set; }
}

public class SupplierLedgerEntryDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class PostSupplierOpeningRequest
{
    /// <summary>Absolute amount &gt; 0.</summary>
    public decimal Amount { get; set; }
    /// <summary>True = له (+ owed to supplier); false = عليه (−).</summary>
    public bool OwedToSupplier { get; set; } = true;
    public string? Note { get; set; }
}

public class PostSupplierPaymentRequest
{
    /// <summary>Absolute amount paid &gt; 0 (stored as negative ledger amount).</summary>
    public decimal Amount { get; set; }
    public string? Method { get; set; }
    public string? Note { get; set; }
    /// <summary>Optional; defaults to UtcNow. Stored in Note/audit only in AP-1 (CreatedAtUtc = now).</summary>
    public DateTime? PaidAtUtc { get; set; }
}

public class PurchaseOrderLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public string? ProductName { get; set; }
    public decimal QtyOrdered { get; set; }
    public decimal QtyReceived { get; set; }
    public decimal QtyRemaining => Math.Max(0, QtyOrdered - QtyReceived);
    /// <summary>Null when caller lacks manage/purchase/financial.view (C4).</summary>
    public decimal? UnitCost { get; set; }
}

public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime OrderedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseOrderLineDto> Lines { get; set; } = new();
}

public class CreatePurchaseOrderLineRequest
{
    public Guid ProductId { get; set; }
    public decimal QtyOrdered { get; set; }
    public decimal UnitCost { get; set; }
}

public class CreatePurchaseOrderRequest
{
    public Guid SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    public string? Notes { get; set; }
    public List<CreatePurchaseOrderLineRequest> Lines { get; set; } = new();
}

public class CreatePoFromSuggestionsRequest
{
    public Guid SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    public string? Notes { get; set; }
    /// <summary>When empty, include all current reorder suggestions.</summary>
    public List<Guid>? ProductIds { get; set; }
}

public class ReceivePurchaseLineRequest
{
    public Guid PurchaseOrderLineId { get; set; }
    public decimal Qty { get; set; }
    public decimal? UnitCost { get; set; }
    public string? BatchNumber { get; set; }
    public DateOnly? ExpiresOn { get; set; }
}

public class ReceivePurchaseOrderRequest
{
    public List<ReceivePurchaseLineRequest> Lines { get; set; } = new();
}

public class GoodsReceiptDto
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? WarehouseCode { get; set; }
    /// <summary>Null when cost redacted.</summary>
    public decimal? TotalAmount { get; set; }
    public string Status { get; set; } = "received";
    public string DocKind { get; set; } = "purchase_doc";
    public List<GoodsReceiptLineDto> Lines { get; set; } = new();
}

/// <summary>AP-2 Buy docs list row — presentation over GoodsReceipt (not a PurchaseInvoice entity).</summary>
public class GoodsReceiptListItemDto
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    /// <summary>Σ qty×unitCost. Null when caller fails cost access.</summary>
    public decimal? TotalAmount { get; set; }
    /// <summary>Always "received" for posted GRNs.</summary>
    public string Status { get; set; } = "received";
    /// <summary>Desk label hint: purchase_doc</summary>
    public string DocKind { get; set; } = "purchase_doc";
}

public class GoodsReceiptLineDto
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Qty { get; set; }
    /// <summary>Null when caller lacks manage/purchase/financial.view (C4).</summary>
    public decimal? UnitCost { get; set; }
    public string? BatchNumber { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public Guid? ProductBatchId { get; set; }
}
