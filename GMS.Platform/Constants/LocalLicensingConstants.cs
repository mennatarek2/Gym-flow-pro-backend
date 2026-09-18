namespace GMS.Platform.Constants;

/// <summary>
/// HyMotion Local Lifetime licensing — entirely separate from SaaS PlatformSubscription/
/// SubscriptionStatuses. A Local license is a one-time issuance, not a recurring subscription;
/// do not merge these concepts (see .wolf/cerebrum.md SaaS-isolation rule for this feature).
/// </summary>
public static class LocalLicenseStatuses
{
    public const string Created = "created";
    public const string PendingActivation = "pending_activation";
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Revoked = "revoked";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Created, PendingActivation, Active, Suspended, Revoked
    };

    /// <summary>The only transitions an ordinary authorized action may perform. Revoked is
    /// terminal — reactivation from Revoked is a distinct, separately-audited admin workflow
    /// (LocalLicenseChangeTypes.Reactivated), never a plain status write.</summary>
    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = new(StringComparer.OrdinalIgnoreCase)
    {
        [Created] = new(StringComparer.OrdinalIgnoreCase) { PendingActivation, Revoked },
        [PendingActivation] = new(StringComparer.OrdinalIgnoreCase) { Active, Revoked },
        [Active] = new(StringComparer.OrdinalIgnoreCase) { Suspended, Revoked, PendingActivation },
        [Suspended] = new(StringComparer.OrdinalIgnoreCase) { Active, Revoked },
        [Revoked] = new(StringComparer.OrdinalIgnoreCase)
    };

    public static bool CanTransition(string from, string to) =>
        AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
}

public static class LocalLicenseChangeTypes
{
    public const string Issued = "issued";
    public const string ActivationRequested = "activation_requested";
    public const string Activated = "activated";
    public const string ActivationRejected = "activation_rejected";
    public const string Suspended = "suspended";
    public const string Revoked = "revoked";
    public const string Reactivated = "reactivated";
    public const string TransferRequested = "transfer_requested";
    public const string TransferCompleted = "transfer_completed";
    public const string TransferRejected = "transfer_rejected";
    public const string DeviceLimitChanged = "device_limit_changed";
    public const string ValidationSucceeded = "validation_succeeded";
    public const string ValidationFailed = "validation_failed";
    public const string ActivationReleased = "activation_released";
}

public static class LocalLicenseInitiators
{
    public const string PlatformAdmin = "platform_admin";
    public const string System = "system";
    public const string Customer = "customer";
}

public static class LocalInstallationStatuses
{
    public const string Active = "active";
    public const string Deactivated = "deactivated";
}

/// <summary>Every /activate and /validate call is logged with one of these, whether it succeeds
/// or not — this is the raw feed suspicious-activity detection reads from (Phase H).</summary>
public static class LocalActivationResults
{
    public const string Success = "success";
    public const string ValidationSuccess = "validation_success";
    public const string InvalidLicenseKey = "invalid_license_key";
    public const string WrongProduct = "wrong_product";
    public const string WrongEdition = "wrong_edition";
    public const string DeviceLimitExceeded = "device_limit_exceeded";
    public const string AlreadyBoundElsewhere = "already_bound_elsewhere";
    public const string LicenseRevoked = "license_revoked";
    public const string LicenseSuspended = "license_suspended";
    public const string LicenseNotYetIssued = "license_not_yet_issued";
    public const string TamperedRequest = "tampered_request";
    public const string RateLimited = "rate_limited";
    public const string CheckSuccess = "check_success";
    public const string Released = "released";
}

public static class LocalLifecycleEventTypes
{
    public const string SetupStarted = "SetupStarted";
    public const string SetupCompleted = "SetupCompleted";
    public const string NewGymStarted = "NewGymStarted";
    public const string BackupVerified = "BackupVerified";
    public const string OldGymRetired = "OldGymRetired";
    public const string NewGymProvisioned = "NewGymProvisioned";
    public const string ProvisionFailed = "ProvisionFailed";
    public const string RestoreStarted = "RestoreStarted";
    public const string RestoreCompleted = "RestoreCompleted";
    public const string RestoreFailed = "RestoreFailed";
    public const string InstallationActivated = "InstallationActivated";
    public const string LicenseActivationFailed = "LicenseActivationFailed";
    public const string LicenseReleased = "LicenseReleased";
    public const string OwnerRecoveryStarted = "OwnerRecoveryStarted";
    public const string OwnerRecoveryCompleted = "OwnerRecoveryCompleted";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        SetupStarted, SetupCompleted, NewGymStarted, BackupVerified, OldGymRetired,
        NewGymProvisioned, ProvisionFailed, RestoreStarted, RestoreCompleted, RestoreFailed,
        InstallationActivated, LicenseActivationFailed, LicenseReleased,
        OwnerRecoveryStarted, OwnerRecoveryCompleted,
    };
}
