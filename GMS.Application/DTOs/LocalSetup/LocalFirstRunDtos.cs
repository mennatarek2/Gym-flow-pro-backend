namespace GMS.Application.DTOs.LocalSetup;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Local Edition setup input. LicenseKey is required by LocalFirstRunService for every mode —
/// including owner recovery on an already-licensed PC. GymName is required for New Gym and
/// optional when restoring access to an existing gym.
/// </summary>
public class LocalFirstRunRequest
{
    /// <summary><c>existingGym</c> or <c>newGym</c>. When omitted, incomplete setup with a gym
    /// infers existingGym; a clean install infers newGym. Completed setup never infers newGym.</summary>
    [MaxLength(32)]
    public string? Mode { get; set; }

    [MaxLength(200)]
    public string? GymName { get; set; }

    [MaxLength(200)]
    public string? GymNameAr { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(40)]
    public string? PhoneNumber { get; set; }

    [Required, MaxLength(200)]
    public string OwnerFullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string OwnerEmail { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string OwnerPassword { get; set; } = string.Empty;

    /// <summary>Required for both Existing Gym and New Gym. A previously activated PC is not a
    /// reason to skip this field — see LocalFirstRunService.EvaluateLicenseAsync.</summary>
    [MaxLength(40)]
    public string? LicenseKey { get; set; }

    /// <summary>Must equal <see cref="LocalSetupModes.NewGymConfirmPhrase"/> when replacing an
    /// existing local gym. Ignored for Existing Gym and for a clean install.</summary>
    [MaxLength(40)]
    public string? ConfirmPhrase { get; set; }
}

public class LocalLicenseValidationRequest
{
    [MaxLength(32)]
    public string? Mode { get; set; }

    [MaxLength(40)]
    public string? LicenseKey { get; set; }
}

public class LocalLicenseValidationResponse
{
    public bool Valid { get; set; }
    public string State { get; set; } = LocalSetupLicenseStates.NotActivated;
    public string Message { get; set; } = string.Empty;
    public string? LicenseKeyMasked { get; set; }
    public string? InstallationId { get; set; }
}

public class LocalBackupPrepareResponse
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? BackupLocation { get; set; }
    public string? BackupId { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
}

public class LocalSetupLicenseInfo
{
    public string State { get; set; } = LocalSetupLicenseStates.NotActivated;
    public string? LicenseKeyMasked { get; set; }
    public string? InstallationId { get; set; }
    public DateTime? LastConfirmedAtUtc { get; set; }
    public string? Edition { get; set; }
}

public class LocalFirstRunStatusResponse
{
    public bool IsCompleted { get; set; }
    public bool SetupRequired { get; set; }
    public Guid? TenantId { get; set; }
    public string? GymName { get; set; }
    public string? GymCode { get; set; }
    public Guid? OwnerUserId { get; set; }
    public bool OwnerMissing { get; set; }
    public bool CanRestoreExistingGym { get; set; }
    public bool CanStartNewGym { get; set; } = true;
    /// <summary>True when a live gym already has an Owner. New Gym then requires that Owner's JWT.</summary>
    public bool RequiresOwnerForNewGym { get; set; }
    /// <summary>SQL catalog this process is using — so Setup can show if it is not the desk database.</summary>
    public string? RuntimeDatabase { get; set; }
    public LocalSetupLicenseInfo? License { get; set; }
    public string? LicenseState { get; set; }
    public string? BackupId { get; set; }
    public string? BackupLocation { get; set; }
}

public static class LocalSetupModes
{
    public const string ExistingGym = "existingGym";
    public const string NewGym = "newGym";
    public const string NewGymConfirmPhrase = "START NEW GYM";
}

/// <summary>Who is calling Local setup complete. Anonymous is allowed only for a blank PC
/// or Existing Gym owner recovery.</summary>
public sealed record LocalSetupActor(bool IsOwner, Guid? TenantId)
{
    public static LocalSetupActor Anonymous { get; } = new(false, null);
}

public static class LocalSetupLicenseStates
{
    public const string Active = "active";
    public const string OfflineValid = "offline_valid";
    public const string NotActivated = "not_activated";
    public const string Expired = "expired";
    public const string ServerUnavailable = "server_unavailable";
    public const string Invalid = "invalid_license";
    public const string NotRegistered = "not_registered";
}

public static class LocalSetupErrorCodes
{
    public const string LicenseRequired = "license_required";
    public const string InvalidLicense = "invalid_license";
    public const string LicenseServerUnavailable = "server_unavailable";
    public const string LicenseExpired = "expired";
    public const string LicenseNotActivated = "not_activated";
    public const string InstallationNotRegistered = "not_registered";
    public const string ExistingGym = "existing_gym";
    public const string BackupFailed = "backup_failed";
    public const string ConfirmationFailed = "confirmation_failed";
    public const string ResetFailed = "reset_failed";
    public const string OwnerCreateFailed = "owner_create_failed";
    public const string GymCreateFailed = "gym_create_failed";
    public const string ActivationFailed = "activation_failed";
    public const string OwnerRequired = "owner_required";
    public const string UseExistingGym = "use_existing_gym";
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
}
