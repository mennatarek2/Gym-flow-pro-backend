namespace GMS.Core.Entities;

/// <summary>
/// One-time staff-issued code for Member App activation (claim).
/// Plaintext is never persisted — only <see cref="CodeHash"/>.
/// Independent of MemberNumber / attendance barcode.
/// </summary>
public class MemberAppActivationCode : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid MemberId { get; set; }

    /// <summary>SHA-256 hex of normalized code + pepper. Never store plaintext.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Identity user id (JWT sub) of the staff who generated the code.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Optimistic concurrency for one-time consume races.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Tenant? Tenant { get; set; }
    public GymMember? Member { get; set; }

    public bool IsActive(DateTime utcNow) =>
        ConsumedAtUtc == null
        && RevokedAtUtc == null
        && ExpiresAtUtc > utcNow;
}
