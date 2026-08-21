namespace GMS.Core.Entities;

/// <summary>
/// Stage 0 member store request. Operational fulfillment only — does not create Sale/Payment
/// and does not write the stock ledger. POS remains the financial + inventory deduction SoT.
/// </summary>
public class MemberOrder : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid MemberId { get; set; }

    /// <summary>Human-readable per-tenant number (e.g. MO-20260810-0007).</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>pending | accepted | rejected | ready | completed</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Fulfillment warehouse snapshotted at create (default WH). Availability checked here only.</summary>
    public Guid WarehouseId { get; set; }

    public string Currency { get; set; } = "EGP";
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }

    public string? MemberNotes { get; set; }
    public string? RejectionReason { get; set; }

    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? ReadyAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }

    public Guid? AcceptedByUserId { get; set; }
    public Guid? ReadyByUserId { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public Guid? RejectedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Tenant? Tenant { get; set; }
    public GymMember? Member { get; set; }
    public Warehouse? Warehouse { get; set; }
    public AppUser? AcceptedByUser { get; set; }
    public AppUser? ReadyByUser { get; set; }
    public AppUser? CompletedByUser { get; set; }
    public AppUser? RejectedByUser { get; set; }
    public ICollection<MemberOrderLine> Lines { get; set; } = new List<MemberOrderLine>();
}
