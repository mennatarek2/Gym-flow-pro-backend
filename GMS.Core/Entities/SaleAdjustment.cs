namespace GMS.Core.Entities;

/// <summary>
/// An auditable non-payment adjustment to a sale balance, such as a write-off
/// or approved cancellation. It never changes collected payment facts.
/// </summary>
public sealed class SaleAdjustment : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid SaleId { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = "write_off";
    public string Status { get; set; } = "posted";
    public string Reason { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }

    public Tenant? Tenant { get; set; }
    public Sale? Sale { get; set; }
    public AppUser? CreatedByUser { get; set; }
}
