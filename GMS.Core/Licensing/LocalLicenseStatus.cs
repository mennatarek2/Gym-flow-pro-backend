namespace GMS.Core.Licensing;

/// <summary>Local-side computed license status - never trust a client's own "IsUsable" claim
/// server-side; this exists purely to drive the Local app's own UI/gating.</summary>
public class LocalLicenseStatus
{
    public bool HasLicense { get; set; }
    public bool SignatureValid { get; set; }

    /// <summary>True while within LocalLicenseClientService.GracePeriodDays of the last
    /// successful online activation/validation.</summary>
    public bool WithinGracePeriod { get; set; }

    /// <summary>True once past GracePeriodDays with no successful online check - soft
    /// enforcement only (see LocalLicenseClientService remarks); never a hard, permanent lockout
    /// from a local clock reading alone.</summary>
    public bool GracePeriodExceeded { get; set; }

    public string? LicenseKey { get; set; }
    public string? Edition { get; set; }
    public DateTime? LastConfirmedAtUtc { get; set; }
    public string InstallationId { get; set; } = string.Empty;

    public bool IsUsable => HasLicense && SignatureValid && !GracePeriodExceeded;
}
