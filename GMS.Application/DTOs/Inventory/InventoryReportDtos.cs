namespace GMS.Application.DTOs.Inventory;

public class InventorySummaryReportDto
{
    /// <summary>Null when caller lacks reports.financial.view.</summary>
    /// <summary>Sellable inventory value Σ(Available × CostPrice); null without financial.view.</summary>
    public decimal? InventoryValueEgp { get; set; }

    public int OutOfStockCount { get; set; }
    public int LowStockCount { get; set; }

    /// <summary>Counts of batches with ExpiresOn within each configured window (days).</summary>
    public List<InventoryExpiryWindowDto> ExpiringSoon { get; set; } = new();

    public bool IncludesValuation { get; set; }

    /// <summary>Cairo calendar-day retail sale units (LineType=retail). Always for inventory.view.</summary>
    public decimal TodayRetailUnits { get; set; }

    /// <summary>Cairo-day retail LineTotal sum; null without reports.financial.view.</summary>
    public decimal? TodayRetailSalesEgp { get; set; }

    /// <summary>POs in draft | approved | partially_received.</summary>
    public int PendingPoCount { get; set; }

    public int InTransitTransferCount { get; set; }
}

public class InventoryExpiryWindowDto
{
    public int Days { get; set; }
    public int BatchCount { get; set; }
}

public class InventoryMovementQueryRequest
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? Reason { get; set; }
    public int Take { get; set; } = 200;
}

public class InventoryMovementReportRowDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public string? ProductName { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public Guid? BatchId { get; set; }
    public decimal QtyDelta { get; set; }
    public decimal? UnitCost { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Note { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public class InventoryReorderSuggestionDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? ImageUrl { get; set; }
    public decimal OnHand { get; set; }
    /// <summary>Sellable qty (excludes expired). Preferred desk signal for Need.</summary>
    public decimal Available { get; set; }
    public decimal ReorderMinQty { get; set; }
    public decimal SuggestedQty { get; set; }
    /// <summary>Omitted/null unless caller has inventory.manage or inventory.purchase.</summary>
    public decimal? CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    /// <summary>30d lookback average daily sale units (server).</summary>
    public decimal AvgDailySales { get; set; }
    /// <summary>Available / AvgDailySales when velocity &gt; 0; otherwise null.</summary>
    public decimal? DaysOfCover { get; set; }
    /// <summary>Open PO remaining + in-transit transfer qty (server).</summary>
    public decimal IncomingOpenQty { get; set; }
}

public class InventoryDeadStockRowDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? ImageUrl { get; set; }
    public decimal OnHand { get; set; }
    public decimal? CostPrice { get; set; }
    public DateTime? LastSoldAtUtc { get; set; }
    public int DaysIdle { get; set; }
}

public class InventoryProductPerformanceRowDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? ImageUrl { get; set; }
    public decimal QtySold { get; set; }
    public decimal RevenueEgp { get; set; }
    /// <summary>Null when caller lacks reports.financial.view.</summary>
    public decimal? EstMarginEgp { get; set; }
    public DateTime? LastSoldAtUtc { get; set; }
}

/// <summary>Result of one daily low-stock/expiry notify pass for a tenant.</summary>
public class InventoryAlertJobResultDto
{
    public Guid TenantId { get; set; }
    public int LowStockNotified { get; set; }
    public int ExpiryNotified { get; set; }
    public int SkippedDedupe { get; set; }
}
