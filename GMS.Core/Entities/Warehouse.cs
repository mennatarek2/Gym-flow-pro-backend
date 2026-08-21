namespace GMS.Core.Entities;

/// <summary>
/// Tenant stock location (INVS-2). Not a Branch — optional <see cref="BranchId"/> reserved for future Branch CRUD.
/// </summary>
public class Warehouse : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Reserved for future Branch entity — no FK yet (conflict C2/C12).</summary>
    public Guid? BranchId { get; set; }

    public Tenant? Tenant { get; set; }
}
