namespace GMS.Platform.Services;

using GMS.Core.Licensing;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

/// <summary>
/// Core HyMotion Local licensing logic: issuance, lifecycle transitions, and - the security-
/// critical part - activation/anti-resale enforcement (rule 16).
///
/// Anti-resale model: a license's device slots are exactly its currently-ACTIVE LocalInstallation
/// rows. Activating from an installation-id that already holds an active slot is a no-op re-
/// confirmation (reinstalling on the SAME PC must always work). Activating from a NEW, different
/// installation-id only succeeds if the active-slot count is below DeviceLimit; for the Lifetime
/// edition default (DeviceLimit=1), that means the second gym's activation attempt is rejected
/// outright while the first gym's installation stays active - this is the literal mechanism that
/// makes "same installer + same license -> Gym B" fail per rule 16, enforced entirely server-side
/// (ActivateAsync never trusts anything the client claims about its own prior state).
/// </summary>
public class LocalLicenseService : ILocalLicenseService
{
    private readonly ILocalLicenseWriteRepository _repo;
    private readonly ILicenseSigningService _signing;
    private readonly PlatformDbContext? _db;

    public LocalLicenseService(
        ILocalLicenseWriteRepository repo,
        ILicenseSigningService signing,
        PlatformDbContext? db = null)
    {
        _repo = repo;
        _signing = signing;
        _db = db;
    }

    public async Task<LocalLicense> IssueAsync(IssueLocalLicenseRequest request, Guid issuedByPlatformAdminUserId, CancellationToken cancellationToken = default)
    {
        if (request.CustomerId is not Guid cid || cid == Guid.Empty)
            throw new ArgumentException("CustomerId is required.", nameof(request));
        if (_db == null)
            throw new InvalidOperationException("Customer linkage requires PlatformDbContext.");

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, cancellationToken)
            ?? throw new ArgumentException("Customer was not found.");
        var customerName = string.IsNullOrWhiteSpace(request.CustomerName)
            ? customer.BusinessName
            : request.CustomerName.Trim();
        Guid? contractId = request.ContractId;
        if (contractId is Guid ctid)
        {
            var contract = await _db.Contracts.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == ctid && c.CustomerId == cid, cancellationToken)
                ?? throw new ArgumentException("Contract does not belong to this customer.", nameof(request));
            if (string.Equals(contract.Status, PlatformContractStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Cannot issue a license against a cancelled contract.");
        }
        if (request.DeviceLimit <= 0)
            throw new ArgumentException("DeviceLimit must be positive.", nameof(request));

        string? contact = request.CustomerContact?.Trim();
        if (string.IsNullOrWhiteSpace(contact))
            contact = customer.Email ?? customer.Phone ?? customer.WhatsApp;

        var license = new LocalLicense
        {
            LicenseKey = GenerateLicenseKey(),
            CustomerName = customerName,
            CustomerContact = contact,
            DealReference = request.DealReference?.Trim(),
            CustomerId = cid,
            ContractId = contractId,
            Edition = string.IsNullOrWhiteSpace(request.Edition) ? "Lifetime" : request.Edition.Trim(),
            DeviceLimit = request.DeviceLimit,
            Notes = request.Notes?.Trim(),
            Status = LocalLicenseStatuses.PendingActivation,
            IssuedAtUtc = DateTime.UtcNow,
            IssuedByPlatformAdminUserId = issuedByPlatformAdminUserId,
        };

        var change = new LocalLicenseChange
        {
            ChangeType = LocalLicenseChangeTypes.Issued,
            FromStatus = LocalLicenseStatuses.Created,
            ToStatus = LocalLicenseStatuses.PendingActivation,
            InitiatedBy = LocalLicenseInitiators.PlatformAdmin,
            PlatformAdminUserId = issuedByPlatformAdminUserId,
        };

        await _repo.SaveAsync(license, change, cancellationToken: cancellationToken);
        return license;
    }

    public async Task<List<LocalLicenseListItemDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var licenses = await _repo.ListAsync(cancellationToken);
        var activeInstalls = await _repo.ListActiveInstallationsAsync(cancellationToken);
        var byLicense = activeInstalls
            .GroupBy(i => i.LicenseId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LocalInstallation>)g.ToList());

        return licenses
            .Select(l => ToListItem(l, byLicense.GetValueOrDefault(l.Id)))
            .ToList();
    }

    public async Task<LocalLicenseDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var license = await _repo.GetByIdAsync(id, includeInstallations: true, cancellationToken);
        if (license == null) return null;

        var summary = SummarizeInstallations(license.Installations);
        var dto = new LocalLicenseDetailDto
        {
            Id = license.Id,
            LicenseKey = license.LicenseKey,
            CustomerId = license.CustomerId,
            ContractId = license.ContractId,
            CustomerName = license.CustomerName,
            CustomerContact = license.CustomerContact,
            DealReference = license.DealReference,
            Edition = license.Edition,
            Status = license.Status,
            DeviceLimit = license.DeviceLimit,
            ActiveInstallationCount = summary.ActiveCount,
            IssuedAtUtc = license.IssuedAtUtc,
            CreatedAtUtc = license.CreatedAtUtc,
            LastValidatedAtUtc = summary.LastValidatedAtUtc,
            InstallationStatus = summary.InstallationStatus,
            GymCode = summary.GymCode,
            GymName = summary.GymName,
            AppVersion = summary.AppVersion,
            Notes = license.Notes,
            RevokedReason = license.RevokedReason,
            RevokedAtUtc = license.RevokedAtUtc,
            Installations = license.Installations
                .OrderByDescending(i => i.FirstActivatedAtUtc)
                .Select(i => new LocalInstallationDto
                {
                    Id = i.Id,
                    InstallationId = i.InstallationId,
                    Status = i.Status,
                    FirstActivatedAtUtc = i.FirstActivatedAtUtc,
                    LastValidatedAtUtc = i.LastValidatedAtUtc,
                    DeactivatedAtUtc = i.DeactivatedAtUtc,
                    DeactivationReason = i.DeactivationReason,
                    GymCode = i.GymCode,
                    GymName = i.GymName,
                    AppVersion = i.AppVersion,
                })
                .ToList(),
            RecentChanges = license.Changes
                .OrderByDescending(c => c.CreatedAtUtc)
                .Take(20)
                .Select(c => new LocalLicenseChangeDto
                {
                    ChangeType = c.ChangeType,
                    FromStatus = c.FromStatus,
                    ToStatus = c.ToStatus,
                    InitiatedBy = c.InitiatedBy,
                    Reason = c.Reason,
                    CreatedAtUtc = c.CreatedAtUtc,
                })
                .ToList(),
            TransferCount = license.Changes.Count(c => c.ChangeType == LocalLicenseChangeTypes.TransferCompleted),
            SuspiciousEventCount = 0,
        };
        if (_db != null)
        {
            dto.SuspiciousEventCount = await _db.LocalActivationAttempts.AsNoTracking()
                .CountAsync(a => a.LicenseId == license.Id && (
                    a.Result == LocalActivationResults.DeviceLimitExceeded
                    || a.Result == LocalActivationResults.AlreadyBoundElsewhere
                    || a.Result == LocalActivationResults.TamperedRequest), cancellationToken);
        }
        if (_db != null && license.ContractId is Guid contractId)
        {
            dto.ContractNumber = await _db.Contracts.AsNoTracking()
                .Where(c => c.Id == contractId)
                .Select(c => c.ContractNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (_db != null)
        {
            dto.RecentOperations = await _db.LocalLifecycleEvents.AsNoTracking()
                .Where(e => e.LicenseId == license.Id)
                .OrderByDescending(e => e.CreatedAtUtc)
                .Take(50)
                .Select(e => new LocalLifecycleEventDto
                {
                    OperationId = e.OperationId,
                    EventType = e.EventType,
                    InstallationId = e.InstallationId,
                    GymCode = e.GymCode,
                    GymName = e.GymName,
                    Message = e.Message,
                    CreatedAtUtc = e.CreatedAtUtc,
                })
                .ToListAsync(cancellationToken);
        }
        return dto;
    }

    public async Task<LocalLifecycleEventDto> RecordLifecycleEventAsync(
        RecordLocalLifecycleEventRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_db == null)
            throw new InvalidOperationException("Lifecycle events require PlatformDbContext.");

        var key = request.LicenseKey?.Trim() ?? "";
        var installationId = request.InstallationId?.Trim() ?? "";
        var eventType = request.EventType?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(installationId) || request.OperationId == Guid.Empty)
            throw new ArgumentException("licenseKey, installationId, and operationId are required.");
        if (!LocalLifecycleEventTypes.All.Contains(eventType))
            throw new ArgumentException("Unknown event type.");

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"{request.OperationId:N}:{eventType}"
            : request.IdempotencyKey.Trim();
        if (idempotencyKey.Length > 80)
            idempotencyKey = idempotencyKey[..80];

        var existing = await _db.LocalLifecycleEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing != null)
        {
            return new LocalLifecycleEventDto
            {
                OperationId = existing.OperationId,
                EventType = existing.EventType,
                InstallationId = existing.InstallationId,
                GymCode = existing.GymCode,
                GymName = existing.GymName,
                Message = existing.Message,
                CreatedAtUtc = existing.CreatedAtUtc,
            };
        }

        var license = await _db.LocalLicenses
            .Include(l => l.Installations)
            .FirstOrDefaultAsync(l => l.LicenseKey == key, cancellationToken)
            ?? throw new ArgumentException("License was not found.");

        var bound = license.Installations.FirstOrDefault(i =>
            string.Equals(i.InstallationId, installationId, StringComparison.OrdinalIgnoreCase));
        var otherActive = license.Installations.FirstOrDefault(i =>
            i.Status == LocalInstallationStatuses.Active
            && !string.Equals(i.InstallationId, installationId, StringComparison.OrdinalIgnoreCase));
        if (bound == null && otherActive != null)
            throw new ArgumentException("Installation does not match this license.");

        if (bound != null)
            ApplyReportedIdentity(bound, request.GymCode, request.GymName, request.AppVersion);

        var row = new LocalLifecycleEvent
        {
            OperationId = request.OperationId,
            IdempotencyKey = idempotencyKey,
            EventType = eventType,
            LicenseId = license.Id,
            InstallationId = installationId,
            CustomerId = license.CustomerId,
            GymCode = string.IsNullOrWhiteSpace(request.GymCode) ? bound?.GymCode : request.GymCode.Trim(),
            GymName = string.IsNullOrWhiteSpace(request.GymName) ? bound?.GymName : request.GymName.Trim(),
            Message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };
        _db.LocalLifecycleEvents.Add(row);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(row).State = EntityState.Detached;
            var raced = await _db.LocalLifecycleEvents.AsNoTracking()
                .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, cancellationToken);
            if (raced != null)
            {
                return new LocalLifecycleEventDto
                {
                    OperationId = raced.OperationId,
                    EventType = raced.EventType,
                    InstallationId = raced.InstallationId,
                    GymCode = raced.GymCode,
                    GymName = raced.GymName,
                    Message = raced.Message,
                    CreatedAtUtc = raced.CreatedAtUtc,
                };
            }

            throw;
        }

        return new LocalLifecycleEventDto
        {
            OperationId = row.OperationId,
            EventType = row.EventType,
            InstallationId = row.InstallationId,
            GymCode = row.GymCode,
            GymName = row.GymName,
            Message = row.Message,
            CreatedAtUtc = row.CreatedAtUtc,
        };
    }

    public async Task<bool> SuspendAsync(Guid licenseId, Guid platformAdminUserId, string reason, CancellationToken cancellationToken = default)
    {
        var license = await _repo.GetByIdAsync(licenseId, cancellationToken: cancellationToken);
        if (license == null) return false;
        if (!LocalLicenseStatuses.CanTransition(license.Status, LocalLicenseStatuses.Suspended)) return false;

        var change = NewChange(license, LocalLicenseChangeTypes.Suspended, LocalLicenseStatuses.Suspended, platformAdminUserId, reason);
        license.Status = LocalLicenseStatuses.Suspended;
        await _repo.SaveAsync(license, change, cancellationToken: cancellationToken);
        return true;
    }

    public async Task<bool> RevokeAsync(Guid licenseId, Guid platformAdminUserId, string reason, CancellationToken cancellationToken = default)
    {
        var license = await _repo.GetByIdAsync(licenseId, cancellationToken: cancellationToken);
        if (license == null) return false;
        if (!LocalLicenseStatuses.CanTransition(license.Status, LocalLicenseStatuses.Revoked)) return false;

        var change = NewChange(license, LocalLicenseChangeTypes.Revoked, LocalLicenseStatuses.Revoked, platformAdminUserId, reason);
        license.Status = LocalLicenseStatuses.Revoked;
        license.RevokedAtUtc = DateTime.UtcNow;
        license.RevokedReason = reason;
        await _repo.SaveAsync(license, change, cancellationToken: cancellationToken);
        return true;
    }

    public async Task<bool> ReactivateAsync(Guid licenseId, Guid platformAdminUserId, string reason, CancellationToken cancellationToken = default)
    {
        // Explicit, separately-audited workflow (rule: Revoked -> Active must NOT happen through
        // an ordinary user action). This is the one place that transition is allowed, and only
        // for a platform admin with a documented reason.
        var license = await _repo.GetByIdAsync(licenseId, cancellationToken: cancellationToken);
        if (license == null) return false;
        if (license.Status != LocalLicenseStatuses.Revoked && license.Status != LocalLicenseStatuses.Suspended) return false;
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A documented reason is required to reactivate a revoked/suspended license.", nameof(reason));

        var change = NewChange(license, LocalLicenseChangeTypes.Reactivated, LocalLicenseStatuses.Active, platformAdminUserId, reason);
        license.Status = LocalLicenseStatuses.Active;
        license.RevokedAtUtc = null;
        license.RevokedReason = null;
        await _repo.SaveAsync(license, change, cancellationToken: cancellationToken);
        return true;
    }

    public async Task<bool> AuthorizeTransferAsync(Guid licenseId, string? oldInstallationId, Guid platformAdminUserId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A documented reason is required to authorize a transfer.", nameof(reason));

        var license = await _repo.GetByIdAsync(licenseId, cancellationToken: cancellationToken);
        if (license == null) return false;

        var activeInstallations = await _repo.GetActiveInstallationsAsync(licenseId, cancellationToken);
        var toDeactivate = string.IsNullOrWhiteSpace(oldInstallationId)
            ? activeInstallations
            : activeInstallations.Where(i => i.InstallationId == oldInstallationId).ToList();

        if (toDeactivate.Count == 0) return false;

        foreach (var installation in toDeactivate)
        {
            installation.Status = LocalInstallationStatuses.Deactivated;
            installation.DeactivatedAtUtc = DateTime.UtcNow;
            installation.DeactivationReason = $"Authorized transfer: {reason}";

            // This phase has no separate pending-approval step - an admin authorizing a transfer
            // immediately deactivates the old device, which is the meaningful licensing event
            // (the new device's later activation is just a normal Activated entry). So this is
            // logged as Completed, not Requested - TransferRequested is reserved for a future
            // self-service "customer asks for a transfer" workflow that does not exist yet.
            var change = NewChange(license, LocalLicenseChangeTypes.TransferCompleted, license.Status, platformAdminUserId, reason);
            await _repo.SaveAsync(license, change, installation, cancellationToken: cancellationToken);
        }

        return true;
    }

    public async Task<ActivationResultDto> ActivateAsync(string licenseKey, string installationId, string? machineFingerprint, string? ipAddress, CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? tx = null;
        try
        {
            if (_db != null && _db.Database.IsRelational())
                tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            var gate = await GateActivationAsync(licenseKey, installationId, ipAddress, cancellationToken, lockLicense: tx != null);
            if (gate.Rejected != null)
            {
                if (tx != null)
                    await tx.CommitAsync(cancellationToken);
                return gate.Rejected;
            }

            var license = gate.License!;
            var installation = gate.ExistingBinding ?? new LocalInstallation
            {
                LicenseId = license.Id,
                InstallationId = installationId,
                Status = LocalInstallationStatuses.Active,
                FirstActivatedAtUtc = DateTime.UtcNow,
                MachineFingerprint = machineFingerprint,
            };
            installation.Status = LocalInstallationStatuses.Active;
            installation.LastValidatedAtUtc = DateTime.UtcNow;

            var wasNotYetActive = license.Status != LocalLicenseStatuses.Active;
            var fromStatus = license.Status;
            license.Status = LocalLicenseStatuses.Active;

            var payload = new LocalLicensePayload
            {
                LicenseId = license.Id,
                LicenseKey = license.LicenseKey,
                Product = license.Product,
                Edition = license.Edition,
                InstallationId = installationId,
                DeviceLimit = license.DeviceLimit,
                IssuedAtUtc = DateTime.UtcNow,
                LicenseVersion = license.LicenseVersion,
            };
            payload.Signature = _signing.Sign(payload);

            var change = NewChange(
                license,
                LocalLicenseChangeTypes.Activated,
                LocalLicenseStatuses.Active,
                null,
                wasNotYetActive ? "First activation" : "Re-activation on existing installation",
                LocalLicenseInitiators.Customer);
            change.FromStatus = fromStatus;

            var attempt = NewAttempt(license.Id, licenseKey, installationId, ipAddress, LocalActivationResults.Success);

            await _repo.SaveAsync(license, change, installation, attempt, cancellationToken);
            if (tx != null)
                await tx.CommitAsync(cancellationToken);

            return new ActivationResultDto { Success = true, Result = LocalActivationResults.Success, License = payload };
        }
        catch
        {
            if (tx != null)
                await tx.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (tx != null)
                await tx.DisposeAsync();
        }
    }

    public async Task<ActivationResultDto> CheckAsync(string licenseKey, string installationId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var gate = await GateActivationAsync(licenseKey, installationId, ipAddress, cancellationToken);
        if (gate.Rejected != null)
            return gate.Rejected;

        await _repo.LogAttemptAsync(
            NewAttempt(gate.License!.Id, licenseKey, installationId, ipAddress, LocalActivationResults.CheckSuccess),
            cancellationToken);

        return new ActivationResultDto
        {
            Success = true,
            Result = LocalActivationResults.CheckSuccess,
            Message = "This license can be used on this installation. It is not activated until gym setup completes.",
        };
    }

    public async Task<ActivationResultDto> ReleaseAsync(string licenseKey, string installationId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey) || string.IsNullOrWhiteSpace(installationId))
            return Rejected(LocalActivationResults.TamperedRequest, "License key and installation id are required.");

        var license = await _repo.GetByKeyAsync(licenseKey.Trim(), cancellationToken);
        if (license == null)
            return Rejected(LocalActivationResults.InvalidLicenseKey, "License key not recognized.");

        var installation = await _repo.GetInstallationAsync(license.Id, installationId, cancellationToken);
        if (installation == null)
        {
            await _repo.LogAttemptAsync(
                NewAttempt(license.Id, licenseKey, installationId, ipAddress, LocalActivationResults.Released),
                cancellationToken);
            return new ActivationResultDto { Success = true, Result = LocalActivationResults.Released, Message = "No device slot was held for this installation." };
        }

        installation.Status = LocalInstallationStatuses.Deactivated;
        installation.DeactivatedAtUtc = DateTime.UtcNow;

        var remainingActive = (await _repo.GetActiveInstallationsAsync(license.Id, cancellationToken))
            .Count(i => i.Id != installation.Id);
        var fromStatus = license.Status;
        if (remainingActive == 0 && license.Status == LocalLicenseStatuses.Active)
            license.Status = LocalLicenseStatuses.PendingActivation;

        var change = NewChange(
            license,
            LocalLicenseChangeTypes.ActivationReleased,
            license.Status,
            null,
            "Local gym setup did not finish; device slot released.",
            LocalLicenseInitiators.Customer);
        change.FromStatus = fromStatus;

        var attempt = NewAttempt(license.Id, licenseKey, installationId, ipAddress, LocalActivationResults.Released);
        await _repo.SaveAsync(license, change, installation, attempt, cancellationToken);

        return new ActivationResultDto { Success = true, Result = LocalActivationResults.Released, Message = "This installation's license slot was released." };
    }

    async Task<ActivationGate> GateActivationAsync(
        string licenseKey, string installationId, string? ipAddress, CancellationToken cancellationToken, bool lockLicense = false)
    {
        if (string.IsNullOrWhiteSpace(licenseKey) || string.IsNullOrWhiteSpace(installationId))
        {
            await _repo.LogAttemptAsync(NewAttempt(null, licenseKey, installationId, ipAddress, LocalActivationResults.TamperedRequest), cancellationToken);
            return ActivationGate.Fail(Rejected(LocalActivationResults.TamperedRequest, "License key and installation id are required."));
        }

        var license = await _repo.GetByKeyAsync(licenseKey.Trim(), cancellationToken);
        if (license == null)
        {
            await _repo.LogAttemptAsync(NewAttempt(null, licenseKey, installationId, ipAddress, LocalActivationResults.InvalidLicenseKey), cancellationToken);
            return ActivationGate.Fail(Rejected(LocalActivationResults.InvalidLicenseKey, "License key not recognized."));
        }

        if (lockLicense)
        {
            await _repo.LockLicenseRowAsync(license.Id, cancellationToken);
            if (_db != null)
            {
                var entry = _db.Entry(license);
                if (entry.State != EntityState.Detached)
                    await entry.ReloadAsync(cancellationToken);
            }
        }

        if (!string.Equals(license.Product, "HyMotion", StringComparison.OrdinalIgnoreCase))
        {
            await _repo.LogAttemptAsync(NewAttempt(license.Id, licenseKey, installationId, ipAddress, LocalActivationResults.WrongProduct), cancellationToken);
            return ActivationGate.Fail(Rejected(LocalActivationResults.WrongProduct, "License is not valid for this product."));
        }

        if (license.Status == LocalLicenseStatuses.Revoked)
        {
            await _repo.LogAttemptAsync(NewAttempt(license.Id, licenseKey, installationId, ipAddress, LocalActivationResults.LicenseRevoked), cancellationToken);
            return ActivationGate.Fail(Rejected(LocalActivationResults.LicenseRevoked, "This license has been revoked."));
        }

        if (license.Status == LocalLicenseStatuses.Suspended)
        {
            await _repo.LogAttemptAsync(NewAttempt(license.Id, licenseKey, installationId, ipAddress, LocalActivationResults.LicenseSuspended), cancellationToken);
            return ActivationGate.Fail(Rejected(LocalActivationResults.LicenseSuspended, "This license is currently suspended."));
        }

        var existingBinding = await _repo.GetInstallationAsync(license.Id, installationId, cancellationToken);
        var isSameMachineReactivating = existingBinding != null && existingBinding.Status == LocalInstallationStatuses.Active;

        if (!isSameMachineReactivating)
        {
            var activeCount = (await _repo.GetActiveInstallationsAsync(license.Id, cancellationToken)).Count;
            if (activeCount >= license.DeviceLimit)
            {
                await _repo.LogAttemptAsync(NewAttempt(license.Id, licenseKey, installationId, ipAddress, LocalActivationResults.DeviceLimitExceeded), cancellationToken);
                return ActivationGate.Fail(Rejected(
                    LocalActivationResults.DeviceLimitExceeded,
                    "This license is already active on another PC. Ask HyMotion support to transfer it, or finish setup on the PC that already holds it."));
            }
        }

        return new ActivationGate(null, license, existingBinding);
    }

    private sealed record ActivationGate(ActivationResultDto? Rejected, LocalLicense? License, LocalInstallation? ExistingBinding)
    {
        public static ActivationGate Fail(ActivationResultDto rejected) => new(rejected, null, null);
    }

    public async Task<ActivationResultDto> ValidateAsync(
        string licenseKey,
        string installationId,
        string? ipAddress,
        CancellationToken cancellationToken = default,
        string? gymCode = null,
        string? gymName = null,
        string? appVersion = null)
    {
        var license = await _repo.GetByKeyAsync(licenseKey?.Trim() ?? string.Empty, cancellationToken);
        if (license == null)
        {
            await _repo.LogAttemptAsync(NewAttempt(null, licenseKey ?? string.Empty, installationId, ipAddress, LocalActivationResults.InvalidLicenseKey), cancellationToken);
            return Rejected(LocalActivationResults.InvalidLicenseKey, "License key not recognized.");
        }

        if (license.Status == LocalLicenseStatuses.Revoked)
        {
            await _repo.LogAttemptAsync(NewAttempt(license.Id, licenseKey!, installationId, ipAddress, LocalActivationResults.LicenseRevoked), cancellationToken);
            return Rejected(LocalActivationResults.LicenseRevoked, "This license has been revoked.");
        }

        if (license.Status == LocalLicenseStatuses.Suspended)
        {
            await _repo.LogAttemptAsync(NewAttempt(license.Id, licenseKey!, installationId, ipAddress, LocalActivationResults.LicenseSuspended), cancellationToken);
            return Rejected(LocalActivationResults.LicenseSuspended, "This license is currently suspended.");
        }

        var installation = await _repo.GetInstallationAsync(license.Id, installationId, cancellationToken);
        if (installation == null || installation.Status != LocalInstallationStatuses.Active)
        {
            // Someone else's activation, or an authorized transfer moved this license elsewhere.
            await _repo.LogAttemptAsync(NewAttempt(license.Id, licenseKey!, installationId, ipAddress, LocalActivationResults.AlreadyBoundElsewhere), cancellationToken);
            return Rejected(LocalActivationResults.AlreadyBoundElsewhere, "This installation is no longer bound to this license.");
        }

        installation.LastValidatedAtUtc = DateTime.UtcNow;
        ApplyReportedIdentity(installation, gymCode, gymName, appVersion);
        var change = NewChange(license, LocalLicenseChangeTypes.ValidationSucceeded, license.Status, null, "Periodic validation", LocalLicenseInitiators.System);
        var attempt = NewAttempt(license.Id, licenseKey!, installationId, ipAddress, LocalActivationResults.ValidationSuccess);

        var payload = new LocalLicensePayload
        {
            LicenseId = license.Id,
            LicenseKey = license.LicenseKey,
            Product = license.Product,
            Edition = license.Edition,
            InstallationId = installationId,
            DeviceLimit = license.DeviceLimit,
            IssuedAtUtc = DateTime.UtcNow,
            LicenseVersion = license.LicenseVersion,
        };
        payload.Signature = _signing.Sign(payload);

        await _repo.SaveAsync(license, change, installation, attempt, cancellationToken);
        return new ActivationResultDto { Success = true, Result = LocalActivationResults.ValidationSuccess, License = payload };
    }

    private static ActivationResultDto Rejected(string result, string message) =>
        new() { Success = false, Result = result, Message = message };

    private static LocalLicenseChange NewChange(LocalLicense license, string changeType, string? toStatus, Guid? platformAdminUserId, string? reason, string initiatedBy = LocalLicenseInitiators.PlatformAdmin) =>
        new()
        {
            LicenseId = license.Id,
            ChangeType = changeType,
            FromStatus = license.Status,
            ToStatus = toStatus,
            InitiatedBy = initiatedBy,
            PlatformAdminUserId = platformAdminUserId,
            Reason = reason,
        };

    private static LocalActivationAttempt NewAttempt(Guid? licenseId, string licenseKeyAttempted, string? installationId, string? ipAddress, string result) =>
        new()
        {
            LicenseId = licenseId,
            LicenseKeyAttempted = licenseKeyAttempted,
            InstallationId = installationId,
            IpAddress = ipAddress,
            Result = result,
        };

    private static LocalLicenseListItemDto ToListItem(LocalLicense l, IReadOnlyList<LocalInstallation>? installs)
    {
        var summary = SummarizeInstallations(installs);
        return new()
        {
            Id = l.Id,
            LicenseKey = l.LicenseKey,
            CustomerId = l.CustomerId,
            ContractId = l.ContractId,
            CustomerName = l.CustomerName,
            Edition = l.Edition,
            Status = l.Status,
            DeviceLimit = l.DeviceLimit,
            ActiveInstallationCount = summary.ActiveCount,
            IssuedAtUtc = l.IssuedAtUtc,
            CreatedAtUtc = l.CreatedAtUtc,
            LastValidatedAtUtc = summary.LastValidatedAtUtc,
            InstallationStatus = summary.InstallationStatus,
            GymCode = summary.GymCode,
            GymName = summary.GymName,
            AppVersion = summary.AppVersion,
        };
    }

    private static (int ActiveCount, DateTime? LastValidatedAtUtc, string? InstallationStatus, string? GymCode, string? GymName, string? AppVersion) SummarizeInstallations(
        IEnumerable<LocalInstallation>? installs)
    {
        var all = (installs ?? Array.Empty<LocalInstallation>()).ToList();
        var active = all
            .Where(i => string.Equals(i.Status, LocalInstallationStatuses.Active, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.LastValidatedAtUtc ?? DateTime.MinValue)
            .ThenByDescending(i => i.FirstActivatedAtUtc)
            .ToList();
        var current = active.FirstOrDefault();
        var identity = current
            ?? all
                .OrderByDescending(i => i.LastValidatedAtUtc ?? DateTime.MinValue)
                .ThenByDescending(i => i.FirstActivatedAtUtc)
                .FirstOrDefault();
        return (active.Count, current?.LastValidatedAtUtc, current?.Status, identity?.GymCode, identity?.GymName, identity?.AppVersion);
    }

    private static string GenerateLicenseKey()
    {
        // HY-LCL-XXXXX-XXXXX, uppercase hex from a real CSPRNG (5 bytes/40 bits each half - not
        // sequential/guessable, and enough headroom that a DB-level unique-index collision at any
        // realistic license volume is not a practical concern).
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(5);
        var hex = Convert.ToHexString(bytes); // 10 hex chars
        return $"HY-LCL-{hex[..5]}-{hex[5..]}";
    }

    /// <summary>Observational identity from the bound installation. Empty values never clear a
    /// stored gym code/name/version. Does not change license status.</summary>
    private static void ApplyReportedIdentity(LocalInstallation installation, string? gymCode, string? gymName, string? appVersion)
    {
        if (!string.IsNullOrWhiteSpace(gymCode))
            installation.GymCode = Clip(gymCode, 40);
        if (!string.IsNullOrWhiteSpace(gymName))
            installation.GymName = Clip(gymName, 200);
        if (!string.IsNullOrWhiteSpace(appVersion))
            installation.AppVersion = Clip(appVersion, 40);
    }

    private static string Clip(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
