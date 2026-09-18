namespace GMS.Platform.Entities;

/// <summary>
/// A HyMotion Local Lifetime license. Entirely separate from <see cref="PlatformSubscription"/> —
/// a Local license is a one-time issuance tied to a physical installation, not a recurring SaaS
/// subscription. Do not give this a TenantId or wire it into subscription/billing logic.
///
/// Mirrors PlatformSubscription + SubscriptionChange's shape deliberately (see
/// SubscriptionWriteRepository): every mutation must go through LocalLicenseWriteRepository so an
/// audit-trail row is always written in the same transaction as the status change.
/// </summary>
public class LocalLicense
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-typed-into-the-installer key, e.g. "HY-LCL-8F92-A81D". Unique.</summary>
    public string LicenseKey { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerContact { get; set; }

    /// <summary>Free-text reference to a future Deal record — becomes a real FK once the Sales/
    /// Deal phase lands (rule: "a license must belong to a real customer/deal"). Kept as a plain
    /// string now rather than inventing a placeholder Deal entity ahead of that phase.</summary>
    public string? DealReference { get; set; }

    /// <summary>Optional FK to the commercial Customer. CustomerName stays as the signed/display
    /// snapshot so existing licenses and the signing payload are unchanged.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Optional FK to the commercial Contract that entitled this license.</summary>
    public Guid? ContractId { get; set; }

    public string Product { get; set; } = "HyMotion";
    public string Edition { get; set; } = "Lifetime";

    /// <summary>created | pending_activation | active | suspended | revoked — see LocalLicenseStatuses.</summary>
    public string Status { get; set; } = "created";

    public int DeviceLimit { get; set; } = 1;

    public Guid? IssuedByPlatformAdminUserId { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }
    public string? Notes { get; set; }

    /// <summary>Bumped whenever the signed payload shape changes — the Local client refuses to
    /// verify a license version it doesn't understand rather than guessing.</summary>
    public int LicenseVersion { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<LocalLicenseChange> Changes { get; set; } = new List<LocalLicenseChange>();
    public ICollection<LocalInstallation> Installations { get; set; } = new List<LocalInstallation>();
}

/// <summary>Append-only history of LocalLicense mutations — never write LocalLicense directly
/// without a paired row here (see LocalLicenseWriteRepository.SaveWithChangeAsync).</summary>
public class LocalLicenseChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicenseId { get; set; }

    public string ChangeType { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }

    /// <summary>platform_admin | system | customer — see LocalLicenseInitiators.</summary>
    public string InitiatedBy { get; set; } = "system";

    public Guid? PlatformAdminUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public LocalLicense? License { get; set; }
}
