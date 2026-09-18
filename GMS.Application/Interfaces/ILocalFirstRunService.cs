namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.LocalSetup;

/// <summary>
/// First-run and recovery setup for a Local Edition install. One gym is live at a time.
/// Completed only while an active Owner exists. License validation is required for every mode —
/// a previously activated PC does not skip the license workflow.
/// </summary>
public interface ILocalFirstRunService
{
    Task<LocalFirstRunStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<LocalLicenseValidationResponse> ValidateLicenseAsync(
        LocalLicenseValidationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Creates a verified SQL+uploads backup of the current gym. No-op success when
    /// there is no gym to replace. Does not retire the gym and does not activate a license.</summary>
    Task<LocalBackupPrepareResponse> PrepareBackupAsync(CancellationToken cancellationToken = default);

    Task<Result<LocalFirstRunStatusResponse>> CompleteFirstRunAsync(
        LocalFirstRunRequest request,
        LocalSetupActor? actor = null,
        CancellationToken cancellationToken = default);
}
