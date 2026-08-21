namespace GMS.Application.DTOs.Inventory;

public class StockTransferLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public string? ProductName { get; set; }
    public decimal Qty { get; set; }
    public Guid? BatchId { get; set; }
}

public class StockTransferDto
{
    public Guid Id { get; set; }
    public Guid FromWarehouseId { get; set; }
    public string? FromWarehouseCode { get; set; }
    public Guid ToWarehouseId { get; set; }
    public string? ToWarehouseCode { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<StockTransferLineDto> Lines { get; set; } = new();
}

public class CreateStockTransferLineRequest
{
    public Guid ProductId { get; set; }
    public decimal Qty { get; set; }
    public Guid? BatchId { get; set; }
}

public class CreateStockTransferRequest
{
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }
    public string? Note { get; set; }
    public List<CreateStockTransferLineRequest> Lines { get; set; } = new();
}
