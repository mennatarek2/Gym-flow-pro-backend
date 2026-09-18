namespace GMS.Platform.Entities;

/// <summary>
/// One physical HyMotion Local installation bound to a <see cref="LocalLicense"/>. This is the
/// anti-resale enforcement point: a license can have at most DeviceLimit rows with
/// Status="active" at once (enforced in LocalLicenseService, not just here) — see
/// LocalActivationAttempt for the raw request log that suspicious-activity detection reads.
/// </summary>
public class LocalInstallation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicenseId { get; set; }

    /// <summary>Client-generated identity (e.g. "INS-93A71"), persisted once on the customer's PC
    /// via the same DPAPI (LocalMachine-scope) mechanism as LocalSecretProvider. Not a hardware
    /// serial — see LocalLicensing.md for exactly what this is derived from.</summary>
    public string InstallationId { get; set; } = string.Empty;

    /// <summary>active | deactivated — see LocalInstallationStatuses.</summary>
    public string Status { get; set; } = "active";

    /// <summary>Coarse, non-invasive fingerprint component (hashed Windows MachineGuid) — one
    /// input among several, never the sole gate (rule 18/29: no invasive hardware collection).</summary>
    public string? MachineFingerprint { get; set; }

    public DateTime FirstActivatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastValidatedAtUtc { get; set; }
    public DateTime? DeactivatedAtUtc { get; set; }
    public string? DeactivationReason { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public string? AppVersion { get; set; }

    public LocalLicense? License { get; set; }
}

/// <summary>
/// Raw log of every /activate and /validate call, successful or not — the feed suspicious-
/// activity detection reads (repeated failures, multiple installations in a short window, etc.).
/// Never references anything sensitive; safe to retain indefinitely for pattern detection.
/// </summary>
public class LocalActivationAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? LicenseId { get; set; }
    public string LicenseKeyAttempted { get; set; } = string.Empty;
    public string? InstallationId { get; set; }
    public string? IpAddress { get; set; }

    /// <summary>success | invalid_license_key | wrong_product | ... — see LocalActivationResults.</summary>
    public string Result { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
