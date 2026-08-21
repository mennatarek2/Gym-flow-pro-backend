namespace GMS.Core.Entities;

/// <summary>Frozen product snapshots for a <see cref="MemberOrder"/> line.</summary>
public class MemberOrderLine : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid MemberOrderId { get; set; }
    public Guid ProductId { get; set; }

    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ProductNameAr { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal Qty { get; set; }
    public decimal LineTotal { get; set; }
    public string Currency { get; set; } = "EGP";

    public Tenant? Tenant { get; set; }
    public MemberOrder? MemberOrder { get; set; }
    public Product? Product { get; set; }
}
