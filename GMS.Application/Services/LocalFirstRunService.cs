namespace GMS.Application.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Backup;
using GMS.Application.DTOs.LocalSetup;
using GMS.Application.DTOs.Provisioning;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Core.Licensing;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Local Edition setup. Two explicit modes: restore access to the existing gym (new Owner,
/// data stays) or start a new gym (backup, confirm, retire current tenant, provision).
/// License validation is a required step in both modes. A stored license is never a reason to
/// skip the workflow — Existing Gym may accept an entered key that matches a still-valid
/// offline license when the license server is unreachable.
/// </summary>
public class LocalFirstRunService : ILocalFirstRunService
{
    public const string NewGymConfirmPhrase = LocalSetupModes.NewGymConfirmPhrase;

    /// <summary>Overridable in tests so backup polling does not sleep.</summary>
    public TimeSpan BackupPollDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Overridable in tests for backup-timeout coverage.</summary>
    public TimeSpan BackupWaitTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>Complete will reuse a Healthy verified backup this recent instead of taking a second one.</summary>
    public TimeSpan RecentBackupReuseWindow { get; set; } = TimeSpan.FromMinutes(30);

    private readonly GymFlowProDbContext _db;
    private readonly ITenantProvisioningService _tenantProvisioning;
    private readonly ILocalLicenseClientService _license;
    private readonly IBackupHealthService _backup;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<LocalFirstRunService> _logger;

    public LocalFirstRunService(
        GymFlowProDbContext db,
        ITenantProvisioningService tenantProvisioning,
        ILocalLicenseClientService license,
        IBackupHealthService backup,
        UserManager<ApplicationUser> userManager,
        ILogger<LocalFirstRunService> logger)
    {
        _db = db;
        _tenantProvisioning = tenantProvisioning;
        _license = license;
        _backup = backup;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<LocalFirstRunStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var existing = await FindLiveGymAsync(cancellationToken);
        var license = SnapshotLicense(_license.GetCurrentStatus());

        if (existing == null)
        {
            return new LocalFirstRunStatusResponse
            {
                IsCompleted = false,
                SetupRequired = true,
                OwnerMissing = true,
                CanRestoreExistingGym = false,
                CanStartNewGym = true,
                RequiresOwnerForNewGym = false,
                RuntimeDatabase = CurrentDatabaseName(),
                License = license,
            };
        }

        var hasLiveIdentityOwner = await HasLiveIdentityOwnerAsync(existing.Id);
        var hasAppOwner = await HasAppOwnerAsync(existing.Id, cancellationToken);
        var ownerMissing = !hasLiveIdentityOwner || !hasAppOwner;
        var completed = !ownerMissing;

        return new LocalFirstRunStatusResponse
        {
            IsCompleted = completed,
            SetupRequired = !completed,
            TenantId = existing.Id,
            GymName = existing.Name,
            GymCode = existing.GymCode,
            OwnerMissing = ownerMissing,
            CanRestoreExistingGym = ownerMissing,
            CanStartNewGym = !ownerMissing,
            RequiresOwnerForNewGym = completed,
            RuntimeDatabase = CurrentDatabaseName(),
            License = license,
        };
    }

    public async Task<LocalLicenseValidationResponse> ValidateLicenseAsync(
        LocalLicenseValidationRequest request, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        var mode = ResolveMode(request.Mode, status);
        return await EvaluateLicenseAsync(request.LicenseKey, mode, commitActivation: false, cancellationToken);
    }

    public async Task<LocalBackupPrepareResponse> PrepareBackupAsync(CancellationToken cancellationToken = default)
    {
        var existing = await FindLiveGymAsync(cancellationToken);
        if (existing == null)
        {
            var health = await _backup.GetHealthAsync();
            return new LocalBackupPrepareResponse
            {
                Success = true,
                Message = "No existing gym to back up.",
                BackupLocation = health.BackupLocation,
            };
        }

        return await CreateVerifiedBackupAsync(cancellationToken);
    }

    private static readonly SemaphoreSlim NewGymGate = new(1, 1);

    public async Task<Result<LocalFirstRunStatusResponse>> CompleteFirstRunAsync(
        LocalFirstRunRequest request,
        LocalSetupActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        var mode = ResolveMode(request.Mode, status);
        actor ??= LocalSetupActor.Anonymous;

        if (status.IsCompleted && mode != LocalSetupModes.NewGym)
        {
            _logger.LogInformation(
                "Local setup requested but an Owner already exists (TenantId={TenantId}); returning existing state.",
                status.TenantId);
            return Result<LocalFirstRunStatusResponse>.Success(status, "Setup was already completed.");
        }

        if (mode == LocalSetupModes.ExistingGym)
            return await CompleteExistingGymAsync(request, status, cancellationToken);

        return await CompleteNewGymAsync(request, status, actor, cancellationToken);
    }

    async Task<Result<LocalFirstRunStatusResponse>> CompleteExistingGymAsync(
        LocalFirstRunRequest request,
        LocalFirstRunStatusResponse status,
        CancellationToken cancellationToken)
    {
        if (status.TenantId is not Guid tenantId)
        {
            return Fail(
                LocalSetupErrorCodes.ExistingGym,
                "There is no existing gym on this PC. Choose New Gym to set one up.");
        }

        var storedLicense = _license.GetCurrentStatus();
        var alreadyHeldThisKey = AlreadyHeldThisKey(storedLicense, request.LicenseKey);
        var license = await EvaluateLicenseAsync(request.LicenseKey, LocalSetupModes.ExistingGym, commitActivation: true, cancellationToken);
        if (!license.Valid)
            return Fail(license.State, license.Message);

        var gymName = string.IsNullOrWhiteSpace(request.GymName) ? status.GymName : request.GymName;
        request.GymName = gymName ?? "";

        var operationId = Guid.NewGuid();
        await ReportAsync(
            request.LicenseKey, operationId, LocalLifecycleEventTypes.OwnerRecoveryStarted,
            status.GymCode, status.GymName, null, cancellationToken);

        var created = await RecreateOwnerAsync(tenantId, status.GymCode ?? "", request, cancellationToken);
        if (!created.IsSuccess)
        {
            await ReleaseIfNewlyActivatedAsync(alreadyHeldThisKey, request.LicenseKey, cancellationToken);
            return created;
        }

        created.Data!.LicenseState = license.State;
        created.Data.GymName = status.GymName;
        created.Data.SetupRequired = false;
        created.Data.OwnerMissing = false;
        created.Data.CanRestoreExistingGym = false;
        created.Data.License = SnapshotLicense(_license.GetCurrentStatus());
        created.Data.RuntimeDatabase = CurrentDatabaseName();
        await ReportAsync(
            request.LicenseKey, operationId, LocalLifecycleEventTypes.OwnerRecoveryCompleted,
            status.GymCode, status.GymName, null, cancellationToken);
        return created;
    }

    async Task<Result<LocalFirstRunStatusResponse>> CompleteNewGymAsync(
        LocalFirstRunRequest request,
        LocalFirstRunStatusResponse status,
        LocalSetupActor actor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.GymName))
            return Fail(LocalSetupErrorCodes.GymCreateFailed, "Gym name is required.");

        var gate = AuthorizeNewGym(status, actor);
        if (gate != null)
            return gate;

        var replacing = status.TenantId.HasValue;
        await NewGymGate.WaitAsync(cancellationToken);
        try
        {
            return await CompleteNewGymCoreAsync(request, status, replacing, cancellationToken);
        }
        finally
        {
            NewGymGate.Release();
        }
    }

    static Result<LocalFirstRunStatusResponse>? AuthorizeNewGym(LocalFirstRunStatusResponse status, LocalSetupActor actor)
    {
        if (!status.TenantId.HasValue)
            return null;

        if (status.OwnerMissing)
        {
            return Fail(
                LocalSetupErrorCodes.UseExistingGym,
                "This PC already has a gym. Restore the Owner with Existing Gym. Do not start a new gym.");
        }

        if (!actor.IsOwner || actor.TenantId != status.TenantId)
        {
            return Fail(
                LocalSetupErrorCodes.OwnerRequired,
                "Sign in as this gym's Owner to start a new gym on this PC.");
        }

        return null;
    }

    async Task<Result<LocalFirstRunStatusResponse>> CompleteNewGymCoreAsync(
        LocalFirstRunRequest request,
        LocalFirstRunStatusResponse status,
        bool replacing,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid();
        var oldGymCode = status.GymCode;
        var oldGymName = status.GymName;

        if (replacing)
        {
            if (!string.Equals(request.ConfirmPhrase?.Trim(), NewGymConfirmPhrase, StringComparison.Ordinal))
            {
                return Fail(
                    LocalSetupErrorCodes.ConfirmationFailed,
                    DestructiveConfirmMessage(status.GymName, status.GymCode));
            }

            var backup = await EnsureVerifiedBackupAsync(cancellationToken);
            if (!backup.Success)
                return Fail(backup.ErrorCode ?? LocalSetupErrorCodes.BackupFailed, backup.Message);

            await ReportAsync(
                request.LicenseKey, operationId, LocalLifecycleEventTypes.BackupVerified,
                oldGymCode, oldGymName, backup.Message, cancellationToken);
        }

        await ReportAsync(
            request.LicenseKey, operationId, replacing ? LocalLifecycleEventTypes.NewGymStarted : LocalLifecycleEventTypes.SetupStarted,
            oldGymCode, oldGymName, null, cancellationToken);

        var storedLicense = _license.GetCurrentStatus();
        var alreadyHeldThisKey = AlreadyHeldThisKey(storedLicense, request.LicenseKey);
        var license = await EvaluateLicenseAsync(request.LicenseKey, LocalSetupModes.NewGym, commitActivation: true, cancellationToken);
        if (!license.Valid)
        {
            await ReportAsync(
                request.LicenseKey, operationId, LocalLifecycleEventTypes.LicenseActivationFailed,
                oldGymCode, oldGymName, license.Message, cancellationToken);
            return Fail(license.State, license.Message);
        }

        await ReportAsync(
            request.LicenseKey, operationId, LocalLifecycleEventTypes.InstallationActivated,
            oldGymCode, oldGymName, license.Message, cancellationToken);

        RetiredGymSnapshot? retired = null;
        if (replacing)
        {
            try
            {
                retired = await RetireLiveGymAsync(status.TenantId!.Value, request.OwnerEmail, cancellationToken);
                await ReportAsync(
                    request.LicenseKey, operationId, LocalLifecycleEventTypes.OldGymRetired,
                    oldGymCode, oldGymName, retired.GymCode, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retire the current gym before New Gym setup.");
                await ReleaseIfNewlyActivatedAsync(alreadyHeldThisKey, request.LicenseKey, cancellationToken);
                await ReportAsync(
                    request.LicenseKey, operationId, LocalLifecycleEventTypes.ProvisionFailed,
                    oldGymCode, oldGymName, "The current gym could not be archived.", cancellationToken);
                return Fail(
                    LocalSetupErrorCodes.ResetFailed,
                    "The current gym could not be archived. Nothing was replaced.");
            }
        }

        await FreeOwnerEmailForNewGymAsync(request.OwnerEmail, retired, cancellationToken);

        var provisioned = await ProvisionNewGymAsync(request, cancellationToken);
        if (!provisioned.IsSuccess)
        {
            await ReportAsync(
                request.LicenseKey, operationId, LocalLifecycleEventTypes.ProvisionFailed,
                oldGymCode, oldGymName, provisioned.Error, cancellationToken);
            await ReleaseIfNewlyActivatedAsync(alreadyHeldThisKey, request.LicenseKey, cancellationToken);
            _db.ChangeTracker.Clear();
            if (retired != null)
            {
                try
                {
                    await ReportAsync(
                        request.LicenseKey, operationId, LocalLifecycleEventTypes.RestoreStarted,
                        oldGymCode, oldGymName, null, cancellationToken);
                    await RestoreRetiredGymAsync(retired, cancellationToken);
                    await ReportAsync(
                        request.LicenseKey, operationId, LocalLifecycleEventTypes.RestoreCompleted,
                        oldGymCode, oldGymName, retired.GymCode, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "New Gym provisioning failed and restore of the previous gym also failed. TenantId={TenantId}", retired.TenantId);
                    await ReportAsync(
                        request.LicenseKey, operationId, LocalLifecycleEventTypes.RestoreFailed,
                        oldGymCode, oldGymName, ex.Message, cancellationToken);
                }
            }

            return Fail(
                LocalSetupErrorCodes.GymCreateFailed,
                provisioned.Error ?? "The new gym could not be created. The previous gym was not replaced.");
        }

        var newGymId = provisioned.Data!.TenantId;
        var liveRow = await _db.Tenants.IgnoreQueryFilters()
            .AnyAsync(t => t.Id == newGymId && !t.IsDeleted, cancellationToken);
        if (!liveRow)
        {
            _logger.LogError("New Gym provision returned {TenantId} but dbo.tenants has no live row", newGymId);
            await ReportAsync(
                request.LicenseKey, operationId, LocalLifecycleEventTypes.ProvisionFailed,
                oldGymCode, oldGymName, "Ghost gym row", cancellationToken);
            await ReleaseIfNewlyActivatedAsync(alreadyHeldThisKey, request.LicenseKey, cancellationToken);
            _db.ChangeTracker.Clear();
            if (retired != null)
            {
                try
                {
                    await RestoreRetiredGymAsync(retired, cancellationToken);
                    await ReportAsync(
                        request.LicenseKey, operationId, LocalLifecycleEventTypes.RestoreCompleted,
                        oldGymCode, oldGymName, retired.GymCode, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ghost gym row missing and restore of the previous gym also failed. TenantId={TenantId}", retired.TenantId);
                    await ReportAsync(
                        request.LicenseKey, operationId, LocalLifecycleEventTypes.RestoreFailed,
                        oldGymCode, oldGymName, ex.Message, cancellationToken);
                }
            }

            return Fail(
                LocalSetupErrorCodes.GymCreateFailed,
                "The gym was not saved. Try setup again. / لم يتم حفظ الصالة. أعد الإعداد.");
        }

        provisioned.Data!.LicenseState = license.State;
        provisioned.Data.License = SnapshotLicense(_license.GetCurrentStatus());
        provisioned.Data.RuntimeDatabase = CurrentDatabaseName();
        if (replacing)
        {
            var health = await _backup.GetHealthAsync();
            provisioned.Data.BackupLocation = health.BackupLocation;
            var latest = (await _backup.GetHistoryAsync(1)).FirstOrDefault();
            provisioned.Data.BackupId = latest?.BackupId;
        }

        await ReportAsync(
            request.LicenseKey, operationId, LocalLifecycleEventTypes.NewGymProvisioned,
            provisioned.Data.GymCode, request.GymName, oldGymCode, cancellationToken);
        await ReportAsync(
            request.LicenseKey, operationId, LocalLifecycleEventTypes.SetupCompleted,
            provisioned.Data.GymCode, request.GymName, null, cancellationToken);

        return provisioned;
    }

    async Task<LocalLicenseValidationResponse> EvaluateLicenseAsync(
        string? licenseKey, string mode, bool commitActivation, CancellationToken cancellationToken)
    {
        var key = licenseKey?.Trim() ?? "";
        var stored = _license.GetCurrentStatus();
        var installationId = string.IsNullOrWhiteSpace(stored.InstallationId)
            ? _license.GetOrCreateInstallationId()
            : stored.InstallationId;

        if (string.IsNullOrWhiteSpace(key))
        {
            return new LocalLicenseValidationResponse
            {
                Valid = false,
                State = LocalSetupLicenseStates.NotActivated,
                Message = "A license key is required to set up HyMotion Local.",
                LicenseKeyMasked = MaskKey(stored.LicenseKey),
                InstallationId = installationId,
            };
        }

        var activation = commitActivation
            ? await _license.ActivateAsync(key, cancellationToken)
            : await _license.CheckAsync(key, cancellationToken);
        if (activation.Success)
        {
            return new LocalLicenseValidationResponse
            {
                Valid = true,
                State = LocalSetupLicenseStates.Active,
                Message = commitActivation
                    ? "License activated for this installation."
                    : "This license can be used on this installation. It will activate when setup completes.",
                LicenseKeyMasked = MaskKey(key),
                InstallationId = installationId,
            };
        }

        var code = activation.ErrorCode ?? "";
        var serverUnavailable = code is "not_configured" or "network_error";

        if (IsNotRegistered(code))
            return Invalid(LocalSetupLicenseStates.NotRegistered, InstallationNotRegisteredMessage(), stored, installationId);

        if (IsExpired(code))
            return Invalid(LocalSetupLicenseStates.Expired, LicenseExpiredMessage(), stored, installationId);

        if (!serverUnavailable)
        {
            return Invalid(
                LocalSetupLicenseStates.Invalid,
                activation.ErrorMessage ?? InvalidLicenseMessage(),
                stored,
                installationId);
        }

        if (mode == LocalSetupModes.NewGym)
        {
            return Invalid(
                LocalSetupLicenseStates.ServerUnavailable,
                "The license server could not be reached. A new Gym setup requires an online license activation. Check the connection and try again.",
                stored,
                installationId);
        }

        return EvaluateOfflineExistingGym(key, stored, installationId);
    }

    LocalLicenseValidationResponse EvaluateOfflineExistingGym(
        string enteredKey, LocalLicenseStatus stored, string installationId)
    {
        if (!stored.HasLicense)
        {
            return Invalid(
                LocalSetupLicenseStates.NotActivated,
                "The license server could not be reached. This installation does not have an activated license yet.",
                stored,
                installationId);
        }

        if (!stored.SignatureValid)
        {
            return Invalid(
                LocalSetupLicenseStates.Invalid,
                InvalidLicenseMessage(),
                stored,
                installationId);
        }

        if (!KeysMatch(enteredKey, stored.LicenseKey))
        {
            return Invalid(
                LocalSetupLicenseStates.Invalid,
                InvalidLicenseMessage(),
                stored,
                installationId);
        }

        if (stored.GracePeriodExceeded)
        {
            return Invalid(
                LocalSetupLicenseStates.Expired,
                "This license is past its offline validity period. Connect to the license server and try again.",
                stored,
                installationId);
        }

        return new LocalLicenseValidationResponse
        {
            Valid = true,
            State = LocalSetupLicenseStates.OfflineValid,
            Message = "The license server could not be reached. The key matches the license already on this PC and it is still valid offline.",
            LicenseKeyMasked = MaskKey(stored.LicenseKey),
            InstallationId = installationId,
        };
    }

    async Task<LocalBackupPrepareResponse> EnsureVerifiedBackupAsync(CancellationToken cancellationToken)
    {
        var recent = await FindRecentVerifiedBackupAsync();
        if (recent != null)
        {
            var health = await _backup.GetHealthAsync();
            return new LocalBackupPrepareResponse
            {
                Success = true,
                Message = "A verified backup of the current gym is already available.",
                BackupLocation = health.BackupLocation,
                BackupId = recent.BackupId,
                CreatedAtUtc = recent.CreatedAtUtc,
            };
        }

        return await CreateVerifiedBackupAsync(cancellationToken);
    }

    async Task<LocalBackupPrepareResponse> CreateVerifiedBackupAsync(CancellationToken cancellationToken)
    {
        var health = await _backup.GetHealthAsync();
        var before = await _backup.GetHistoryAsync(30);
        var beforeIds = before.Select(h => h.BackupId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var trigger = await _backup.TriggerManualBackupAsync();
        if (!trigger.Started)
        {
            return new LocalBackupPrepareResponse
            {
                Success = false,
                ErrorCode = LocalSetupErrorCodes.BackupFailed,
                Message = string.IsNullOrWhiteSpace(trigger.Message)
                    ? "A safe backup could not be created. The existing Gym has not been changed."
                    : $"{trigger.Message} The existing Gym has not been changed.",
                BackupLocation = health.BackupLocation,
            };
        }

        var deadline = DateTime.UtcNow + BackupWaitTimeout;
        BackupHistoryItemDto? fresh = null;
        while (DateTime.UtcNow <= deadline)
        {
            if (BackupPollDelay > TimeSpan.Zero)
                await Task.Delay(BackupPollDelay, cancellationToken);

            var history = await _backup.GetHistoryAsync(30);
            fresh = history.FirstOrDefault(h => !beforeIds.Contains(h.BackupId));
            if (fresh == null)
                continue;

            if (IsVerifiedBackup(fresh))
            {
                return new LocalBackupPrepareResponse
                {
                    Success = true,
                    Message = "A verified backup of the current gym was created.",
                    BackupLocation = health.BackupLocation,
                    BackupId = fresh.BackupId,
                    CreatedAtUtc = fresh.CreatedAtUtc,
                };
            }

            if (fresh.Status is "Failed" or "Partial")
            {
                return new LocalBackupPrepareResponse
                {
                    Success = false,
                    ErrorCode = LocalSetupErrorCodes.BackupFailed,
                    Message = "A safe backup could not be created. The existing Gym has not been changed.",
                    BackupLocation = health.BackupLocation,
                    BackupId = fresh.BackupId,
                    CreatedAtUtc = fresh.CreatedAtUtc,
                };
            }
        }

        _logger.LogWarning("Setup backup did not finish in time. LastStatus={Status} BackupId={BackupId}", fresh?.Status, fresh?.BackupId);
        return new LocalBackupPrepareResponse
        {
            Success = false,
            ErrorCode = LocalSetupErrorCodes.BackupFailed,
            Message = "A safe backup could not be created. The existing Gym has not been changed.",
            BackupLocation = health.BackupLocation,
            BackupId = fresh?.BackupId,
        };
    }

    async Task<BackupHistoryItemDto?> FindRecentVerifiedBackupAsync()
    {
        var history = await _backup.GetHistoryAsync(10);
        var cutoff = DateTime.UtcNow - RecentBackupReuseWindow;
        return history.FirstOrDefault(h => IsVerifiedBackup(h) && h.CreatedAtUtc >= cutoff);
    }

    static bool IsVerifiedBackup(BackupHistoryItemDto item) =>
        string.Equals(item.Status, "Healthy", StringComparison.OrdinalIgnoreCase) && item.DatabaseVerified;

    async Task<RetiredGymSnapshot> RetireLiveGymAsync(
        Guid tenantId, string? ownerEmail, CancellationToken cancellationToken)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Live gym disappeared before retirement.");

        var snapshot = new RetiredGymSnapshot
        {
            TenantId = tenant.Id,
            GymCode = tenant.GymCode,
            Name = tenant.Name,
            Email = tenant.Email,
            WasActive = tenant.IsActive,
        };

        // tenants.Email is globally unique, including soft-deleted rows. Keep the owner
        // email free so the new gym can reuse it.
        tenant.Email = RetiredContactEmail(tenant.Id);

        var identities = await LoadIdentitiesForTenantAsync(tenantId);
        await AppendIdentityByEmailAsync(identities, ownerEmail);
        foreach (var identity in identities)
        {
            snapshot.Identities.Add(new RetiredIdentitySnapshot
            {
                User = identity,
                Email = identity.Email,
                UserName = identity.UserName,
                NormalizedEmail = identity.NormalizedEmail,
                NormalizedUserName = identity.NormalizedUserName,
                WasActive = identity.IsActive,
            });

            var retiredEmail = $"retired.{identity.Id:N}@retired.local";
            identity.Email = retiredEmail;
            identity.UserName = retiredEmail;
            identity.NormalizedEmail = retiredEmail.ToUpperInvariant();
            identity.NormalizedUserName = retiredEmail.ToUpperInvariant();
            identity.IsActive = false;
            identity.UpdatedAtUtc = DateTime.UtcNow;
            await _userManager.UpdateAsync(identity);
        }

        tenant.IsActive = false;
        tenant.IsDeleted = true;
        tenant.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Retired live gym {GymCode} ({TenantId}) so a new gym can be provisioned on this PC. Previous data remains in the database and in backup.",
            snapshot.GymCode, snapshot.TenantId);

        return snapshot;
    }

    async Task RestoreRetiredGymAsync(RetiredGymSnapshot snapshot, CancellationToken cancellationToken)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == snapshot.TenantId, cancellationToken);
        if (tenant != null)
        {
            tenant.IsDeleted = false;
            tenant.IsActive = snapshot.WasActive;
            tenant.Email = snapshot.Email;
            tenant.UpdatedAtUtc = DateTime.UtcNow;
        }

        foreach (var identity in snapshot.Identities)
        {
            identity.User.Email = identity.Email;
            identity.User.UserName = identity.UserName;
            identity.User.NormalizedEmail = identity.NormalizedEmail;
            identity.User.NormalizedUserName = identity.NormalizedUserName;
            identity.User.IsActive = identity.WasActive;
            identity.User.UpdatedAtUtc = DateTime.UtcNow;
            await _userManager.UpdateAsync(identity.User);
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Restored previously retired gym {GymCode} ({TenantId}) after New Gym setup failed.", snapshot.GymCode, snapshot.TenantId);
    }

    async Task<List<ApplicationUser>> LoadIdentitiesForTenantAsync(Guid tenantId)
    {
        var byId = new Dictionary<Guid, ApplicationUser>();
        foreach (var user in await _db.Users.Where(u => u.TenantId == tenantId).ToListAsync())
            byId[user.Id] = user;

        foreach (var role in new[] { "Owner", "Manager", "Trainer", "Receptionist", "Employee" })
        {
            foreach (var user in await _userManager.GetUsersInRoleAsync(role))
            {
                if (user.TenantId == tenantId)
                    byId[user.Id] = user;
            }
        }

        return byId.Values.ToList();
    }

    async Task AppendIdentityByEmailAsync(List<ApplicationUser> identities, string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return;

        var leftover = await _userManager.FindByEmailAsync(email.Trim());
        if (leftover != null && identities.All(u => u.Id != leftover.Id))
            identities.Add(leftover);
    }

    async Task FreeOwnerEmailForNewGymAsync(
        string? ownerEmail, RetiredGymSnapshot? alreadyRetired, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerEmail))
            return;

        var email = ownerEmail.Trim();
        var occupied = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Email == email)
            .ToListAsync(cancellationToken);
        foreach (var tenant in occupied)
        {
            if (alreadyRetired != null && tenant.Id == alreadyRetired.TenantId)
                continue;
            tenant.Email = RetiredContactEmail(tenant.Id);
            tenant.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (alreadyRetired == null)
        {
            var leftoverIdentities = new List<ApplicationUser>();
            await AppendIdentityByEmailAsync(leftoverIdentities, email);
            foreach (var identity in leftoverIdentities)
            {
                var retiredEmail = $"retired.{identity.Id:N}@retired.local";
                identity.Email = retiredEmail;
                identity.UserName = retiredEmail;
                identity.NormalizedEmail = retiredEmail.ToUpperInvariant();
                identity.NormalizedUserName = retiredEmail.ToUpperInvariant();
                identity.IsActive = false;
                identity.UpdatedAtUtc = DateTime.UtcNow;
                await _userManager.UpdateAsync(identity);
            }
        }

        if (occupied.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
    }

    static string RetiredContactEmail(Guid tenantId) =>
        $"retired.{tenantId:N}@retired.local";

    async Task<Result<LocalFirstRunStatusResponse>> ProvisionNewGymAsync(
        LocalFirstRunRequest request, CancellationToken cancellationToken)
    {
        var provisionRequest = new ProvisionTenantRequest
        {
            Name = request.GymName!.Trim(),
            NameAr = request.GymNameAr,
            City = string.IsNullOrWhiteSpace(request.City) ? "-" : request.City,
            Address = request.Address,
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? "-" : request.PhoneNumber,
            Email = request.OwnerEmail,
            OwnerFullName = request.OwnerFullName,
            OwnerEmail = request.OwnerEmail,
            OwnerPassword = request.OwnerPassword,
            StartTrial = false,
        };

        var result = await _tenantProvisioning.ProvisionAsync(
            provisionRequest, actorPlatformUserId: Guid.Empty, ipAddress: null, cancellationToken);

        if (!result.IsSuccess || result.Data == null)
        {
            var detail = string.IsNullOrWhiteSpace(result.Message)
                ? result.Error
                : $"{result.Error}: {result.Message}";
            _logger.LogWarning(
                "New Gym provisioning failed. Error={Error} Detail={Detail}",
                result.Error, result.Message);
            return Result<LocalFirstRunStatusResponse>.Failure(
                detail ?? "Setup failed.", LocalSetupErrorCodes.GymCreateFailed);
        }

        return Result<LocalFirstRunStatusResponse>.Success(new LocalFirstRunStatusResponse
        {
            IsCompleted = true,
            SetupRequired = false,
            TenantId = result.Data.TenantId,
            GymName = request.GymName.Trim(),
            GymCode = result.Data.GymCode,
            OwnerUserId = result.Data.OwnerUserId,
            OwnerMissing = false,
            CanRestoreExistingGym = false,
            CanStartNewGym = true,
        });
    }

    async Task<Result<LocalFirstRunStatusResponse>> RecreateOwnerAsync(
        Guid tenantId,
        string gymCode,
        LocalFirstRunRequest request,
        CancellationToken cancellationToken)
    {
        var ownerEmail = request.OwnerEmail.Trim().ToLowerInvariant();
        var nameParts = request.OwnerFullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.FirstOrDefault() ?? request.OwnerFullName.Trim();
        var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : string.Empty;

        var identity = await _userManager.FindByEmailAsync(ownerEmail);
        if (identity == null)
        {
            identity = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = ownerEmail,
                Email = ownerEmail,
                EmailConfirmed = true,
                FirstName = firstName,
                LastName = lastName,
                TenantId = tenantId,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            var createResult = await _userManager.CreateAsync(identity, request.OwnerPassword);
            if (!createResult.Succeeded)
            {
                return Result<LocalFirstRunStatusResponse>.Failure(
                    "Failed to create owner account / فشل إنشاء حساب المالك",
                    LocalSetupErrorCodes.OwnerCreateFailed);
            }
        }
        else
        {
            if (identity.TenantId != Guid.Empty && identity.TenantId != tenantId)
                return Result<LocalFirstRunStatusResponse>.Failure("This email is already used on another gym.", LocalSetupErrorCodes.OwnerCreateFailed);

            identity.TenantId = tenantId;
            identity.IsActive = true;
            identity.FirstName = firstName;
            identity.LastName = lastName;
            identity.UpdatedAtUtc = DateTime.UtcNow;
            await _userManager.UpdateAsync(identity);
            var token = await _userManager.GeneratePasswordResetTokenAsync(identity);
            var reset = await _userManager.ResetPasswordAsync(identity, token, request.OwnerPassword);
            if (!reset.Succeeded)
            {
                return Result<LocalFirstRunStatusResponse>.Failure(
                    "Failed to reset owner password / فشل تحديث كلمة السر",
                    LocalSetupErrorCodes.OwnerCreateFailed);
            }
        }

        if (!await _userManager.IsInRoleAsync(identity, "Owner"))
        {
            var roleResult = await _userManager.AddToRoleAsync(identity, "Owner");
            if (!roleResult.Succeeded)
                return Result<LocalFirstRunStatusResponse>.Failure("Failed to assign Owner role.", LocalSetupErrorCodes.OwnerCreateFailed);
        }

        var identityId = identity.Id.ToString();
        var appUser = await _db.AppUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.UserId == identityId, cancellationToken);
        if (appUser == null)
        {
            _db.AppUsers.Add(new AppUser
            {
                TenantId = tenantId,
                UserId = identityId,
                FirstName = firstName,
                LastName = lastName,
                Email = ownerEmail,
                Role = "Owner",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            appUser.IsDeleted = false;
            appUser.IsActive = true;
            appUser.Role = "Owner";
            appUser.FirstName = firstName;
            appUser.LastName = lastName;
            appUser.Email = ownerEmail;
            appUser.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Recreated Local owner {Email} on existing tenant {TenantId} ({GymCode}).", ownerEmail, tenantId, gymCode);

        return Result<LocalFirstRunStatusResponse>.Success(new LocalFirstRunStatusResponse
        {
            IsCompleted = true,
            TenantId = tenantId,
            GymCode = gymCode,
            OwnerUserId = identity.Id,
        });
    }

    async Task<LiveGym?> FindLiveGymAsync(CancellationToken cancellationToken) =>
        await _db.Tenants
            .Where(t => !t.IsDeleted)
            .Select(t => new LiveGym(t.Id, t.Name, t.GymCode))
            .FirstOrDefaultAsync(cancellationToken);

    sealed record LiveGym(Guid Id, string Name, string GymCode);

    async Task<bool> HasLiveIdentityOwnerAsync(Guid tenantId)
    {
        var identityOwners = await _userManager.GetUsersInRoleAsync("Owner");
        return identityOwners.Any(u => u.TenantId == tenantId && u.IsActive);
    }

    Task<bool> HasAppOwnerAsync(Guid tenantId, CancellationToken cancellationToken) =>
        _db.AppUsers
            .IgnoreQueryFilters()
            .AnyAsync(u =>
                u.TenantId == tenantId
                && u.Role == "Owner"
                && u.IsActive
                && !u.IsDeleted, cancellationToken);

    static string ResolveMode(string? requested, LocalFirstRunStatusResponse status)
    {
        var mode = requested?.Trim();
        if (string.Equals(mode, LocalSetupModes.NewGym, StringComparison.OrdinalIgnoreCase))
            return LocalSetupModes.NewGym;
        if (string.Equals(mode, LocalSetupModes.ExistingGym, StringComparison.OrdinalIgnoreCase))
            return LocalSetupModes.ExistingGym;

        if (status.TenantId.HasValue)
            return LocalSetupModes.ExistingGym;

        return LocalSetupModes.NewGym;
    }

    LocalSetupLicenseInfo SnapshotLicense(LocalLicenseStatus stored)
    {
        var installationId = string.IsNullOrWhiteSpace(stored.InstallationId)
            ? _license.GetOrCreateInstallationId()
            : stored.InstallationId;

        var state = LocalSetupLicenseStates.NotActivated;
        if (stored.HasLicense && !stored.SignatureValid)
            state = LocalSetupLicenseStates.Invalid;
        else if (stored.HasLicense && stored.GracePeriodExceeded)
            state = LocalSetupLicenseStates.Expired;
        else if (stored.HasLicense && stored.SignatureValid)
            state = LocalSetupLicenseStates.OfflineValid;

        return new LocalSetupLicenseInfo
        {
            State = state,
            LicenseKeyMasked = MaskKey(stored.LicenseKey),
            InstallationId = installationId,
            LastConfirmedAtUtc = stored.LastConfirmedAtUtc,
            Edition = stored.Edition,
        };
    }

    async Task ReleaseIfNewlyActivatedAsync(bool alreadyHeldThisKey, string? licenseKey, CancellationToken cancellationToken)
    {
        if (alreadyHeldThisKey || string.IsNullOrWhiteSpace(licenseKey))
            return;

        try
        {
            var released = await _license.ReleaseAsync(licenseKey, cancellationToken);
            if (!released.Success)
            {
                _logger.LogWarning(
                    "Could not release license slot after setup failed. Code={Code} Message={Message}",
                    released.ErrorCode, released.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not release license slot after setup failed.");
        }
    }

    async Task ReportAsync(
        string? licenseKey,
        Guid operationId,
        string eventType,
        string? gymCode,
        string? gymName,
        string? message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            return;
        try
        {
            await _license.ReportLifecycleEventAsync(
                licenseKey, operationId, eventType, gymCode, gymName, message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lifecycle event {EventType} was not delivered to the license server.", eventType);
        }
    }

    string CurrentDatabaseName()
    {
        try
        {
            return _db.Database.GetDbConnection().Database;
        }
        catch
        {
            return string.Empty;
        }
    }

    static LocalLicenseValidationResponse Invalid(
        string state, string message, LocalLicenseStatus stored, string installationId) =>
        new()
        {
            Valid = false,
            State = state,
            Message = message,
            LicenseKeyMasked = MaskKey(stored.LicenseKey),
            InstallationId = installationId,
        };

    static Result<LocalFirstRunStatusResponse> Fail(string errorCode, string message) =>
        Result<LocalFirstRunStatusResponse>.Failure(message, errorCode);

    static string InvalidLicenseMessage() =>
        "The license key is invalid or cannot be activated for this installation.";

    static string LicenseExpiredMessage() =>
        "This license is expired. Enter a valid key and try again.";

    static string InstallationNotRegisteredMessage() =>
        "This installation is not registered for the license key you entered.";

    static string DestructiveConfirmMessage(string? gymName, string? gymCode)
    {
        var identity = string.IsNullOrWhiteSpace(gymName)
            ? (gymCode ?? "the current gym")
            : $"{gymName} ({gymCode})";
        return $"This action will replace the current local Gym setup ({identity}). Your existing business data will only be recoverable from the backup created before continuing. Type {NewGymConfirmPhrase} to confirm.";
    }

    static bool AlreadyHeldThisKey(LocalLicenseStatus stored, string? licenseKey) =>
        stored.HasLicense && stored.SignatureValid && KeysMatch(licenseKey ?? "", stored.LicenseKey);

    static bool KeysMatch(string entered, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return false;
        static string Norm(string value) =>
            new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        return Norm(entered) == Norm(stored);
    }

    static string? MaskKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 4) return null;
        return new string('•', key.Length - 4) + key[^4..];
    }

    static bool IsNotRegistered(string code) =>
        code is "not_found" or "unbound" or "unknown_installation" or "installation_mismatch";

    static bool IsExpired(string code) =>
        code is "expired" or "revoked" or "suspended";

    sealed class RetiredGymSnapshot
    {
        public Guid TenantId { get; set; }
        public string GymCode { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public bool WasActive { get; set; }
        public List<RetiredIdentitySnapshot> Identities { get; } = new();
    }

    sealed class RetiredIdentitySnapshot
    {
        public ApplicationUser User { get; set; } = null!;
        public string? Email { get; set; }
        public string? UserName { get; set; }
        public string? NormalizedEmail { get; set; }
        public string? NormalizedUserName { get; set; }
        public bool WasActive { get; set; }
    }
}
