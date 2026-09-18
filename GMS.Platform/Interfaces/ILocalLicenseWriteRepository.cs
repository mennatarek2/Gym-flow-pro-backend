namespace GMS.Platform.Interfaces;

using GMS.Platform.Entities;

/// <summary>
/// The only sanctioned way to mutate LocalLicense/LocalInstallation - mirrors
/// ISubscriptionWriteRepository.SaveWithChangeAsync exactly (one transaction, entity + audit row
/// together). LocalLicenseService must never call DbContext.SaveChangesAsync directly for a
/// license mutation.
/// </summary>
public interface ILocalLicenseWriteRepository
{
    /// <summary>Persists the license, its change-audit row, and (when given) the installation
    /// row and/or activation-attempt log row, all in one transaction.</summary>
    Task SaveAsync(
        LocalLicense license,
        LocalLicenseChange change,
        LocalInstallation? installation = null,
        LocalActivationAttempt? attempt = null,
        CancellationToken cancellationToken = default);

    Task<LocalLicense?> GetByKeyAsync(string licenseKey, CancellationToken cancellationToken = default);
    Task<LocalLicense?> GetByIdAsync(Guid id, bool includeInstallations = false, CancellationToken cancellationToken = default);
    Task<List<LocalLicense>> ListAsync(CancellationToken cancellationToken = default);
    Task<List<LocalInstallation>> ListActiveInstallationsAsync(CancellationToken cancellationToken = default);
    Task<List<LocalInstallation>> GetActiveInstallationsAsync(Guid licenseId, CancellationToken cancellationToken = default);
    Task<LocalInstallation?> GetInstallationAsync(Guid licenseId, string installationId, CancellationToken cancellationToken = default);

    /// <summary>Takes a SQL Server UPDLOCK on this license row. Must run inside an ambient
    /// transaction so concurrent Activate calls for the same license serialize before the
    /// DeviceLimit recount. Does not lock other licenses.</summary>
    Task LockLicenseRowAsync(Guid licenseId, CancellationToken cancellationToken = default);

    /// <summary>Logs an activation/validation attempt on its own - used for the rejection paths
    /// that never touch the license/installation rows themselves (e.g. license key not found).</summary>
    Task LogAttemptAsync(LocalActivationAttempt attempt, CancellationToken cancellationToken = default);
}
