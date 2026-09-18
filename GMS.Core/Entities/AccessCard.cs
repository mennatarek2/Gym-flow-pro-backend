namespace GMS.Core.Entities;

/// <summary>
/// Physical PVC access card inventory row (additive to GymMember.MemberNumber).
/// Desk barcode encodes <see cref="Code"/>; legacy printed cards use MemberNumber
/// which is backfilled as Code=MemberNumber / Status=Assigned.
/// </summary>
public class AccessCard : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Machine-readable barcode payload. Unique per tenant.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Available | Assigned | Lost | Damaged | Blocked</summary>
    public string Status { get; set; } = "Available";

    public Guid? MemberId { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime? LostAtUtc { get; set; }

    /// <summary>Optional reason for Lost / Damaged / Blocked / Unassign.</summary>
    public string? Reason { get; set; }

    /// <summary>Optional bulk-create batch identifier (GUID string).</summary>
    public string? BatchId { get; set; }

    public Tenant? Tenant { get; set; }
    public GymMember? Member { get; set; }
}
