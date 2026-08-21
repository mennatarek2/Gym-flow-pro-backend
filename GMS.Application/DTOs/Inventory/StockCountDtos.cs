namespace GMS.Application.DTOs.Inventory;

public class StockCountLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public string? ProductName { get; set; }
    public decimal SystemQty { get; set; }
    public decimal CountedQty { get; set; }
    public decimal Variance { get; set; }
}

public class StockCountDto
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CountedAtUtc { get; set; }
    public string? Note { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<StockCountLineDto> Lines { get; set; } = new();
}

public class CreateStockCountRequest
{
    public Guid WarehouseId { get; set; }
    /// <summary>When null/empty, snapshots all active TrackStock products for the tenant.</summary>
    public List<Guid>? ProductIds { get; set; }
    public string? Note { get; set; }
}

public class UpdateStockCountLineRequest
{
    public Guid LineId { get; set; }
    public decimal CountedQty { get; set; }
}

public class UpdateStockCountLinesRequest
{
    public List<UpdateStockCountLineRequest> Lines { get; set; } = new();
}
