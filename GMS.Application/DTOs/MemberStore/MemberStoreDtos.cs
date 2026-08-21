namespace GMS.Application.DTOs.MemberStore;

public class MemberStoreProductDto
{
    public Guid Id { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string? Brand { get; set; }
    public string? ImageUrl { get; set; }
    public string UnitOfMeasure { get; set; } = "pcs";
    public decimal SellPrice { get; set; }
    public string Currency { get; set; } = "EGP";
    public bool AllowFractionalQty { get; set; }
    public bool TrackStock { get; set; }
    /// <summary>Sellable qty at default warehouse (0 when untracked / no warehouse).</summary>
    public decimal AvailableQty { get; set; }
    public bool InStock { get; set; }
}

public class CreateMemberOrderRequest
{
    public string? Notes { get; set; }
    public List<CreateMemberOrderLineRequest> Lines { get; set; } = new();
}

public class CreateMemberOrderLineRequest
{
    public Guid ProductId { get; set; }
    public decimal Qty { get; set; }
}

public class RejectMemberOrderRequest
{
    public string? Reason { get; set; }
}

public class MemberOrderLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ProductNameAr { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Qty { get; set; }
    public decimal LineTotal { get; set; }
    public string Currency { get; set; } = "EGP";
}

public class MemberOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid MemberId { get; set; }
    public string? MemberName { get; set; }
    public string? MemberNumber { get; set; }
    public Guid WarehouseId { get; set; }
    public string Currency { get; set; } = "EGP";
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public string? MemberNotes { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? ReadyAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public List<MemberOrderLineDto> Lines { get; set; } = new();
}

public class MemberOrderListItemDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid MemberId { get; set; }
    public string? MemberName { get; set; }
    public string? MemberNumber { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "EGP";
    public int LineCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
