namespace GMS.Platform.Entities;

/// <summary>
/// Current HyMotion Local customer-sales-contract terms template. Snapshotted onto each issued
/// document. Not gym-member membership terms.
/// </summary>
public class LocalSalesContractTerms
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TermsEn { get; set; } = string.Empty;
    public string TermsAr { get; set; } = string.Empty;
    public Guid? UpdatedByPlatformAdminUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Issued, immutable paper snapshot of a <see cref="PlatformContract"/> at issue time.
/// Later payments update the live sale, not this row. One issued document per sale.
/// </summary>
public class LocalSalesContractDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContractId { get; set; }
    public Guid CustomerId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string Status { get; set; } = "issued";
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid IssuedByPlatformAdminUserId { get; set; }
    public int PrintCount { get; set; }
    public DateTime? LastPrintedAtUtc { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public PlatformContract? Contract { get; set; }
}
