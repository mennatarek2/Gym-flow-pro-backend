namespace GMS.Core.Entities;

/// <summary>
/// Member store fulfillment order. Accept/Ready are operational only (no stock/payment).
/// Complete creates a paid retail <see cref="Sale"/> (stock + invoice) via SaleService and links <see cref="SaleId"/>.
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

    /// <summary>Retail sale created on Complete (stock + invoice). Null until completed successfully.</summary>
    public Guid? SaleId { get; set; }

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
    public Sale? Sale { get; set; }
    public AppUser? AcceptedByUser { get; set; }
    public AppUser? ReadyByUser { get; set; }
    public AppUser? CompletedByUser { get; set; }
    public AppUser? RejectedByUser { get; set; }
    public ICollection<MemberOrderLine> Lines { get; set; } = new List<MemberOrderLine>();
}
