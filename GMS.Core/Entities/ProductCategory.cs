namespace GMS.Core.Entities;

/// <summary>
/// Product category within a tenant catalog (INVS-1).
/// </summary>
public class ProductCategory : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
