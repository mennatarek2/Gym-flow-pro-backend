namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;
using GMS.Platform.Entities;

public interface ILocalLicenseService
{
    /// <summary>Admin/Ops-only. Never callable by a Sales Rep (rule 12) - enforced by the
    /// controller's authorization policy, not by this method, since this is plain business logic
    /// with no access to the current user's role.</summary>
    Task<LocalLicense> IssueAsync(IssueLocalLicenseRequest request, Guid issuedByPlatformAdminUserId, CancellationToken cancellationToken = default);

    Task<List<LocalLicenseListItemDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<LocalLicenseDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> SuspendAsync(Guid licenseId, Guid platformAdminUserId, string reason, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(Guid licenseId, Guid platformAdminUserId, string reason, CancellationToken cancellationToken = default);
    Task<bool> ReactivateAsync(Guid licenseId, Guid platformAdminUserId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Admin-authorized PC replacement: deactivates the named (or all, if null) active
    /// installation(s) for a license so a new one can activate normally through ActivateAsync.
    /// This IS the anti-resale-safe way to move a license - see rule 17.</summary>
    Task<bool> AuthorizeTransferAsync(Guid licenseId, string? oldInstallationId, Guid platformAdminUserId, string reason, CancellationToken cancellationToken = default);

    /// <summary>The customer-facing, anonymous entry point HyMotion Local calls. All anti-resale
    /// enforcement lives here - see class remarks on LocalLicenseService.</summary>
    Task<ActivationResultDto> ActivateAsync(string licenseKey, string installationId, string? machineFingerprint, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Dry-run of ActivateAsync. Confirms the key can be used on this installation
    /// without consuming a device slot or changing license status. Setup Validate must call this,
    /// not ActivateAsync.</summary>
    Task<ActivationResultDto> CheckAsync(string licenseKey, string installationId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Releases this installation's device slot after Local setup failed to create the
    /// gym. Only the matching installationId is deactivated. If no active slots remain, the
    /// license returns to pending_activation.</summary>
    Task<ActivationResultDto> ReleaseAsync(string licenseKey, string installationId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Periodic re-check from an already-activated installation. Does not consume a
    /// device slot and does not bind a new installation - only confirms/refreshes one that
    /// already holds an active binding.</summary>
    Task<ActivationResultDto> ValidateAsync(
        string licenseKey,
        string installationId,
        string? ipAddress,
        CancellationToken cancellationToken = default,
        string? gymCode = null,
        string? gymName = null,
        string? appVersion = null);

    /// <summary>License-key authenticated Local lifecycle observation. Idempotent on IdempotencyKey.
    /// Does not change license status.</summary>
    Task<LocalLifecycleEventDto> RecordLifecycleEventAsync(RecordLocalLifecycleEventRequest request, CancellationToken cancellationToken = default);
}
