namespace GMS.Core.Interfaces;

using GMS.Core.Licensing;

/// <summary>
/// The Local Edition side of activation - calls out to the platform license server, verifies
/// what comes back, and persists it. Never contains the private signing key (see
/// ILicenseVerificationService, which this depends on for signature checks).
/// </summary>
public interface ILocalLicenseClientService
{
    /// <summary>Stable per-install identity, generated once and persisted (not a hardware serial
    /// - see LocalLicenseStore.GenerateInstallationId).</summary>
    string GetOrCreateInstallationId();

    /// <summary>Persisted installation id when the license store already exists. Does not mint a
    /// new identity as a side effect of recovery.</summary>
    string? TryGetInstallationId();

    /// <summary>Last verified license payload, if any. Used only to authenticate to the Platform
    /// recovery endpoints (license key + installation id). Never a password.</summary>
    LocalLicensePayload? GetStoredLicense();

    /// <summary>First-run (or re-run) activation. On success, verifies the signature before ever
    /// persisting the result - a payload that fails verification is discarded, never stored.</summary>
    Task<LocalLicenseActivationOutcome> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default);

    /// <summary>Asks the license server whether this key can be used here without consuming a
    /// device slot. Setup Validate must call this instead of ActivateAsync.</summary>
    Task<LocalLicenseActivationOutcome> CheckAsync(string licenseKey, CancellationToken cancellationToken = default);

    /// <summary>Releases this installation's device slot after gym setup failed.</summary>
    Task<LocalLicenseActivationOutcome> ReleaseAsync(string licenseKey, CancellationToken cancellationToken = default);

    /// <summary>Local, offline-only status computation from whatever is currently stored - never
    /// makes a network call. Safe to call on every request.</summary>
    LocalLicenseStatus GetCurrentStatus();

    /// <summary>Best-effort online re-check against the currently stored license. Never throws on
    /// network failure - a failed revalidation just means GetCurrentStatus keeps using the last
    /// good stored payload until GracePeriodExceeded (see LocalLicenseStatus).</summary>
    Task<bool> TryRevalidateAsync(CancellationToken cancellationToken = default);

    /// <summary>Same check-in as <see cref="TryRevalidateAsync(CancellationToken)"/>, with
    /// optional gym identity for the license server. Default implementation ignores identity.</summary>
    Task<bool> TryRevalidateAsync(string? gymCode, string? gymName, CancellationToken cancellationToken = default)
        => TryRevalidateAsync(cancellationToken);

    /// <summary>Best-effort lifecycle report to the license server. Must never throw; Local
    /// gym operation continues if Platform is unreachable.</summary>
    Task ReportLifecycleEventAsync(
        string licenseKey,
        Guid operationId,
        string eventType,
        string? gymCode = null,
        string? gymName = null,
        string? message = null,
        CancellationToken cancellationToken = default);

    /// <summary>Forwards desk product feedback to the license server so HyMotion Platform can
    /// review it. Returns Success=false when offline / not configured / rejected — never throws.</summary>
    Task<LocalDeskFeedbackReportOutcome> ReportDeskFeedbackAsync(
        LocalDeskFeedbackReportRequest request,
        CancellationToken cancellationToken = default);
}

public class LocalLicenseActivationOutcome
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class LocalDeskFeedbackReportRequest
{
    public Guid TenantId { get; set; }
    public Guid SenderUserId { get; set; }
    public string SenderRole { get; set; } = string.Empty;
    public string? SenderEmail { get; set; }
    public string? SenderDisplayName { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string? PageUrl { get; set; }
    public string? ClientRequestId { get; set; }
}

public class LocalDeskFeedbackReportOutcome
{
    public bool Success { get; set; }
    public Guid? Id { get; set; }
    public bool AlreadySubmitted { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
