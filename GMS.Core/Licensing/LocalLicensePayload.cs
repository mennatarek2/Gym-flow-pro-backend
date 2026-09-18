namespace GMS.Core.Licensing;

/// <summary>
/// The signed payload a HyMotion Local installation receives from the platform license server on
/// /activate and /validate, and stores locally (see LocalRuntimePaths.LicenseFile). Deliberately
/// contains only what the Local app needs to enforce its own license state offline - no server
/// secrets, no customer PII beyond a display name, no signing key material (rule 13/12).
///
/// Lives in GMS.Core (not GMS.Platform) because BOTH sides need the exact same type and canonical
/// serialization to sign/verify against: GMS.Platform (has the private key, references GMS.Core)
/// signs it; GMS.Application/Infrastructure (the Local client, also references GMS.Core, never
/// references GMS.Platform) verifies it. Neither side owns the other.
/// </summary>
public class LocalLicensePayload
{
    public Guid LicenseId { get; set; }
    public string LicenseKey { get; set; } = string.Empty;
    public string Product { get; set; } = "HyMotion";
    public string Edition { get; set; } = "Lifetime";

    /// <summary>The installation this signed payload is bound to. Empty only for a payload issued
    /// before first activation (never actually shipped to a client - activation always binds).</summary>
    public string InstallationId { get; set; } = string.Empty;

    public int DeviceLimit { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public int LicenseVersion { get; set; } = 1;

    /// <summary>Base64 ECDSA (P-256, SHA-256) signature over LicenseCanonicalizer.Build(this).</summary>
    public string Signature { get; set; } = string.Empty;
}
