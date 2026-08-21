namespace GMS.Core.Entities;

/// <summary>
/// Records that a client-supplied idempotency key already produced a <see cref="Sale"/>, so a
/// retried request returns the same result instead of double-selling. Deliberately not a
/// <see cref="BaseEntity"/> — this is a dedup ledger, not a soft-deletable domain record.
/// </summary>
public class SaleIdempotencyKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public Guid SaleId { get; set; }
    public string ResponseHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Sale? Sale { get; set; }
}
