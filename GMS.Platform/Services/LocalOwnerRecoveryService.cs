namespace GMS.Platform.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using GMS.Core.Licensing;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class LocalOwnerRecoveryService : ILocalOwnerRecoveryService
{
    public static readonly TimeSpan ApprovalLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(7);

    private readonly PlatformDbContext _db;
    private readonly ILicenseSigningService _signer;
    private readonly IPlatformAuditService _audit;

    public LocalOwnerRecoveryService(
        PlatformDbContext db,
        ILicenseSigningService signer,
        IPlatformAuditService audit)
    {
        _db = db;
        _signer = signer;
        _audit = audit;
    }

    public async Task<LocalOwnerRecoveryClientDto> RequestFromInstallationAsync(
        LocalOwnerRecoveryAuthRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var license = await AuthenticateLicenseAsync(request, requireActiveInstallation: true, cancellationToken);
        var gymCode = RequireGymCode(request.GymCode, license.Installation.GymCode);
        var nonce = string.IsNullOrWhiteSpace(request.Nonce) ? OwnerRecoveryCodec.NewNonce() : request.Nonce.Trim();
        var requestId = request.RequestId is Guid id && id != Guid.Empty ? id : Guid.NewGuid();

        await CancelOpenRequestsAsync(license.Installation.InstallationId, requestId, "replaced_by_new_request", cancellationToken);

        var existing = await _db.LocalOwnerRecoveries
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);
        if (existing != null)
        {
            if (!SameBinding(existing, license.License.Id, license.Installation.InstallationId, gymCode, nonce))
                throw new InvalidOperationException("This recovery request does not match this installation.");
            await ExpireIfNeededAsync(existing, cancellationToken);
            return ToClient(existing, includeCode: false);
        }

        var row = new LocalOwnerRecoveryRequest
        {
            Id = requestId,
            LicenseId = license.License.Id,
            CustomerId = license.License.CustomerId,
            InstallationId = license.Installation.InstallationId,
            GymCode = gymCode,
            GymName = FirstNonEmpty(request.GymName, license.Installation.GymName),
            Nonce = nonce,
            Status = LocalOwnerRecoveryStatuses.Pending,
            Method = LocalOwnerRecoveryMethods.Online,
            CreatedAtUtc = DateTime.UtcNow,
        };
        AppendHistory(row, "request_created", "installation", "pending", null);
        _db.LocalOwnerRecoveries.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            Guid.Empty,
            "platform.local_owner_recovery.requested",
            after: new { row.Id, row.InstallationId, row.GymCode, ipAddress });
        return ToClient(row, includeCode: false);
    }

    public async Task<LocalOwnerRecoveryClientDto> PollFromInstallationAsync(
        LocalOwnerRecoveryAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        var row = await LoadBoundAsync(request, cancellationToken);
        await ExpireIfNeededAsync(row, cancellationToken);
        var includeCode = row.Status == LocalOwnerRecoveryStatuses.Approved;
        return ToClient(row, includeCode);
    }

    public async Task<LocalOwnerRecoveryClientDto> CompleteFromInstallationAsync(
        LocalOwnerRecoveryAuthRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var row = await LoadBoundAsync(request, cancellationToken);
        await ExpireIfNeededAsync(row, cancellationToken);

        if (row.Status == LocalOwnerRecoveryStatuses.Completed)
            return ToClient(row, includeCode: false);

        if (row.Status != LocalOwnerRecoveryStatuses.Approved)
            throw new InvalidOperationException("This recovery request is not approved.");
        if (row.ApprovalJti is not Guid jti)
            throw new InvalidOperationException("This recovery request is not approved.");
        if (request.Jti is Guid presented && presented != jti)
            throw new InvalidOperationException("This recovery approval does not match this request.");

        row.Status = LocalOwnerRecoveryStatuses.Completed;
        row.CompletedAtUtc = DateTime.UtcNow;
        row.ApprovalBlob = null;
        AppendHistory(row, "password_reset_completed", "installation", "completed", null);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            Guid.Empty,
            "platform.local_owner_recovery.completed",
            after: new { row.Id, row.InstallationId, row.GymCode, ipAddress });
        return ToClient(row, includeCode: false);
    }

    public async Task<IReadOnlyList<LocalOwnerRecoveryListItemDto>> ListAsync(
        Guid? customerId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var query = _db.LocalOwnerRecoveries.AsNoTracking().AsQueryable();
        if (customerId is Guid cid && cid != Guid.Empty)
        {
            query = query.Where(x =>
                x.CustomerId == cid
                || _db.LocalInstallations.Any(i =>
                    i.InstallationId == x.InstallationId
                    && i.GymCode == x.GymCode
                    && _db.LocalLicenses.Any(l => l.Id == i.LicenseId && l.CustomerId == cid)));
        }
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status.Trim());

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
            ExpireInMemory(row);
        return rows.Select(ToListItem).ToList();
    }

    public async Task<LocalOwnerRecoveryDetailDto?> GetAsync(
        Guid id,
        bool includeRecoveryCode,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.LocalOwnerRecoveries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row == null) return null;
        await ExpireIfNeededAsync(row, cancellationToken);
        return ToDetail(row, includeRecoveryCode && row.Status == LocalOwnerRecoveryStatuses.Approved);
    }

    public async Task<LocalOwnerRecoveryDetailDto> ImportChallengeAsync(
        ImportOwnerRecoveryChallengeRequest request,
        Guid actorPlatformUserId,
        CancellationToken cancellationToken = default)
    {
        if (!OwnerRecoveryCodec.TryParseChallenge(request.Challenge, out var challenge))
            throw new ArgumentException("The recovery request could not be read.");

        var installation = await ResolveImportedInstallationAsync(challenge, request.CustomerId, cancellationToken);

        var existing = await _db.LocalOwnerRecoveries
            .FirstOrDefaultAsync(x => x.Id == challenge.RequestId, cancellationToken);
        if (existing != null)
        {
            if (!SameChallengeBinding(existing, challenge.InstallationId, challenge.GymCode, challenge.Nonce))
                throw new InvalidOperationException("This recovery request does not match this installation.");
            await ExpireIfNeededAsync(existing, cancellationToken);
            return ToDetail(existing, includeRecoveryCode: false);
        }

        await CancelOpenRequestsAsync(challenge.InstallationId, challenge.RequestId, "replaced_by_imported_challenge", cancellationToken);

        var row = new LocalOwnerRecoveryRequest
        {
            Id = challenge.RequestId,
            LicenseId = installation.LicenseId,
            CustomerId = installation.License?.CustomerId,
            InstallationId = challenge.InstallationId,
            GymCode = challenge.GymCode,
            GymName = FirstNonEmpty(challenge.GymName, installation.GymName),
            Nonce = challenge.Nonce,
            Status = LocalOwnerRecoveryStatuses.Pending,
            Method = LocalOwnerRecoveryMethods.Offline,
            CreatedAtUtc = DateTime.UtcNow,
        };
        AppendHistory(row, "challenge_imported", actorPlatformUserId.ToString("D"), "pending", null);
        _db.LocalOwnerRecoveries.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.local_owner_recovery.imported",
            after: new { row.Id, row.InstallationId, row.GymCode });
        return ToDetail(row, includeRecoveryCode: false);
    }

    public async Task<LocalOwnerRecoveryDetailDto> ApproveAsync(
        Guid id,
        OwnerRecoveryDecisionRequest request,
        Guid actorPlatformUserId,
        CancellationToken cancellationToken = default)
    {
        var reason = RequireReason(request.Reason);
        var row = await RequireRowAsync(id, cancellationToken);
        await ExpireIfNeededAsync(row, cancellationToken);
        if (row.Status == LocalOwnerRecoveryStatuses.Approved)
            return ToDetail(row, includeRecoveryCode: true);
        EnsureTransition(row.Status, LocalOwnerRecoveryStatuses.Approved);

        var method = string.Equals(request.Method, LocalOwnerRecoveryMethods.Offline, StringComparison.OrdinalIgnoreCase)
            ? LocalOwnerRecoveryMethods.Offline
            : LocalOwnerRecoveryMethods.Online;

        var payload = new OwnerRecoveryPayload
        {
            RequestId = row.Id,
            InstallationId = row.InstallationId,
            GymCode = row.GymCode,
            Nonce = row.Nonce,
            Jti = Guid.NewGuid(),
            ExpiresAtUtc = DateTime.UtcNow.Add(ApprovalLifetime),
        };
        var signature = _signer.Sign(OwnerRecoveryCanonicalizer.Build(payload));
        var blob = OwnerRecoveryCodec.FormatApproval(payload, signature);

        row.Status = LocalOwnerRecoveryStatuses.Approved;
        row.Method = method;
        row.ApprovedAtUtc = DateTime.UtcNow;
        row.ExpiresAtUtc = payload.ExpiresAtUtc;
        row.ApprovalJti = payload.Jti;
        row.ApprovalBlob = blob;
        row.DecisionByPlatformUserId = actorPlatformUserId;
        row.DecisionReason = reason;
        AppendHistory(row, "approved", actorPlatformUserId.ToString("D"), method, reason);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.local_owner_recovery.approved",
            after: new { row.Id, row.InstallationId, row.GymCode, method, expiresAtUtc = row.ExpiresAtUtc });
        return ToDetail(row, includeRecoveryCode: true);
    }

    public async Task<LocalOwnerRecoveryDetailDto> RejectAsync(
        Guid id,
        OwnerRecoveryDecisionRequest request,
        Guid actorPlatformUserId,
        CancellationToken cancellationToken = default)
    {
        var reason = RequireReason(request.Reason);
        var row = await RequireRowAsync(id, cancellationToken);
        await ExpireIfNeededAsync(row, cancellationToken);
        EnsureTransition(row.Status, LocalOwnerRecoveryStatuses.Rejected);
        row.Status = LocalOwnerRecoveryStatuses.Rejected;
        row.RejectedAtUtc = DateTime.UtcNow;
        row.DecisionByPlatformUserId = actorPlatformUserId;
        row.DecisionReason = reason;
        row.ApprovalBlob = null;
        AppendHistory(row, "rejected", actorPlatformUserId.ToString("D"), "rejected", reason);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.local_owner_recovery.rejected",
            after: new { row.Id, row.InstallationId, row.GymCode });
        return ToDetail(row, includeRecoveryCode: false);
    }

    public async Task<LocalOwnerRecoveryDetailDto> RevokeAsync(
        Guid id,
        OwnerRecoveryDecisionRequest request,
        Guid actorPlatformUserId,
        CancellationToken cancellationToken = default)
    {
        var reason = RequireReason(request.Reason);
        var row = await RequireRowAsync(id, cancellationToken);
        await ExpireIfNeededAsync(row, cancellationToken);
        EnsureTransition(row.Status, LocalOwnerRecoveryStatuses.Revoked);
        row.Status = LocalOwnerRecoveryStatuses.Revoked;
        row.RevokedAtUtc = DateTime.UtcNow;
        row.DecisionByPlatformUserId = actorPlatformUserId;
        row.DecisionReason = reason;
        row.ApprovalBlob = null;
        AppendHistory(row, "revoked", actorPlatformUserId.ToString("D"), "revoked", reason);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.local_owner_recovery.revoked",
            after: new { row.Id, row.InstallationId, row.GymCode });
        return ToDetail(row, includeRecoveryCode: false);
    }

    async Task<LocalInstallation> ResolveImportedInstallationAsync(
        OwnerRecoveryChallenge challenge,
        Guid? expectedCustomerId,
        CancellationToken cancellationToken)
    {
        var matches = await _db.LocalInstallations
            .Include(x => x.License)
            .Where(x => x.InstallationId == challenge.InstallationId)
            .ToListAsync(cancellationToken);
        if (matches.Count == 0)
            throw new InvalidOperationException("No active installation matches this recovery request.");

        LocalInstallation? Pick(IEnumerable<LocalInstallation> rows) =>
            rows
                .OrderByDescending(x => x.Status == "active")
                .ThenByDescending(x => string.Equals(x.GymCode, challenge.GymCode, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.LastValidatedAtUtc)
                .FirstOrDefault();

        LocalInstallation? installation;
        if (expectedCustomerId is Guid cid && cid != Guid.Empty)
        {
            installation = Pick(matches.Where(x => x.License?.CustomerId == cid));
            if (installation == null
                || IdentityBelongsToAnotherCustomer(matches, cid, challenge.GymCode, installation))
            {
                var other = Pick(matches.Where(x =>
                    string.Equals(x.GymCode, challenge.GymCode, StringComparison.OrdinalIgnoreCase)));
                var label = FirstNonEmpty(other?.GymName, other?.GymCode);
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(label)
                        ? "This recovery request does not belong to this customer."
                        : $"This recovery request does not belong to this customer. It matches {label}.");
            }
        }
        else
        {
            installation = Pick(matches.Where(x => x.Status == "active")) ?? Pick(matches);
        }

        if (installation == null)
            throw new InvalidOperationException("No active installation matches this recovery request.");
        if (!string.IsNullOrWhiteSpace(installation.GymCode)
            && !string.Equals(installation.GymCode, challenge.GymCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This recovery request does not match the recorded gym.");
        return installation;
    }

    static bool IdentityBelongsToAnotherCustomer(
        IReadOnlyList<LocalInstallation> matches,
        Guid expectedCustomerId,
        string gymCode,
        LocalInstallation onCustomer)
    {
        if (!string.IsNullOrWhiteSpace(onCustomer.GymCode))
            return false;
        if (string.IsNullOrWhiteSpace(gymCode))
            return false;
        return matches.Any(x =>
            x.License?.CustomerId is Guid other
            && other != expectedCustomerId
            && string.Equals(x.GymCode, gymCode, StringComparison.OrdinalIgnoreCase));
    }

    async Task<(LocalLicense License, LocalInstallation Installation)> AuthenticateLicenseAsync(
        LocalOwnerRecoveryAuthRequest request,
        bool requireActiveInstallation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.InstallationId))
            throw new ArgumentException("licenseKey and installationId are required.");

        var license = await _db.LocalLicenses
            .Include(x => x.Installations)
            .FirstOrDefaultAsync(x => x.LicenseKey == request.LicenseKey.Trim(), cancellationToken)
            ?? throw new InvalidOperationException("This recovery request could not be submitted.");

        var installation = license.Installations
            .FirstOrDefault(x => string.Equals(x.InstallationId, request.InstallationId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (installation == null)
            throw new InvalidOperationException("This recovery request could not be submitted.");
        if (requireActiveInstallation && installation.Status != "active")
            throw new InvalidOperationException("This recovery request could not be submitted.");

        return (license, installation);
    }

    async Task<LocalOwnerRecoveryRequest> LoadBoundAsync(
        LocalOwnerRecoveryAuthRequest request,
        CancellationToken cancellationToken)
    {
        var license = await AuthenticateLicenseAsync(request, requireActiveInstallation: false, cancellationToken);
        if (request.RequestId is not Guid requestId || requestId == Guid.Empty)
            throw new ArgumentException("requestId is required.");
        var row = await _db.LocalOwnerRecoveries.FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken)
            ?? throw new InvalidOperationException("This recovery request could not be found.");
        var gymCode = string.IsNullOrWhiteSpace(request.GymCode) ? row.GymCode : request.GymCode.Trim();
        if (!SameBinding(row, license.License.Id, license.Installation.InstallationId, gymCode, row.Nonce))
            throw new InvalidOperationException("This recovery request does not match this installation.");
        if (!string.IsNullOrWhiteSpace(request.Nonce) && !string.Equals(request.Nonce.Trim(), row.Nonce, StringComparison.Ordinal))
            throw new InvalidOperationException("This recovery request does not match this installation.");
        return row;
    }

    async Task<LocalOwnerRecoveryRequest> RequireRowAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.LocalOwnerRecoveries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new InvalidOperationException("Recovery request not found.");

    async Task CancelOpenRequestsAsync(string installationId, Guid keepId, string reason, CancellationToken cancellationToken)
    {
        var open = await _db.LocalOwnerRecoveries
            .Where(x =>
                x.InstallationId == installationId
                && x.Id != keepId
                && (x.Status == LocalOwnerRecoveryStatuses.Pending || x.Status == LocalOwnerRecoveryStatuses.Approved))
            .ToListAsync(cancellationToken);
        foreach (var row in open)
        {
            if (row.Status == LocalOwnerRecoveryStatuses.Approved)
                throw new InvalidOperationException("An approved recovery is already outstanding. Revoke or wait for it to expire.");
            row.Status = LocalOwnerRecoveryStatuses.Cancelled;
            row.ApprovalBlob = null;
            AppendHistory(row, "cancelled", "system", "cancelled", reason);
        }
    }

    async Task ExpireIfNeededAsync(LocalOwnerRecoveryRequest row, CancellationToken cancellationToken)
    {
        if (!ExpireInMemory(row)) return;
        await _db.SaveChangesAsync(cancellationToken);
    }

    static bool ExpireInMemory(LocalOwnerRecoveryRequest row)
    {
        var now = DateTime.UtcNow;
        if (row.Status == LocalOwnerRecoveryStatuses.Pending && row.CreatedAtUtc.Add(PendingLifetime) < now)
        {
            row.Status = LocalOwnerRecoveryStatuses.Expired;
            row.ApprovalBlob = null;
            AppendHistory(row, "expired", "system", "expired", "pending_timeout");
            return true;
        }
        if (row.Status == LocalOwnerRecoveryStatuses.Approved
            && row.ExpiresAtUtc is DateTime expires
            && OwnerRecoveryCodec.IsExpired(expires, now))
        {
            row.Status = LocalOwnerRecoveryStatuses.Expired;
            row.ApprovalBlob = null;
            AppendHistory(row, "expired", "system", "expired", "approval_timeout");
            return true;
        }
        return false;
    }

    static bool SameBinding(
        LocalOwnerRecoveryRequest row,
        Guid licenseId,
        string installationId,
        string gymCode,
        string nonce) =>
        row.LicenseId == licenseId
        && SameChallengeBinding(row, installationId, gymCode, nonce);

    static bool SameChallengeBinding(
        LocalOwnerRecoveryRequest row,
        string installationId,
        string gymCode,
        string nonce) =>
        string.Equals(row.InstallationId, installationId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.GymCode, gymCode, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.Nonce, nonce, StringComparison.Ordinal);

    static string RequireGymCode(string? requested, string? recorded)
    {
        var gym = (requested ?? string.Empty).Trim();
        if (gym.Length == 0) gym = (recorded ?? string.Empty).Trim();
        if (gym.Length == 0)
            throw new ArgumentException("Gym code is required.");
        if (!string.IsNullOrWhiteSpace(recorded)
            && !string.Equals(recorded.Trim(), gym, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This recovery request does not match the recorded gym.");
        return gym;
    }

    static string RequireReason(string? reason)
    {
        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length < 10)
            throw new ArgumentException("Reason must be at least 10 characters.");
        return trimmed;
    }

    static void EnsureTransition(string from, string to)
    {
        var ok = (from, to) switch
        {
            (LocalOwnerRecoveryStatuses.Pending, LocalOwnerRecoveryStatuses.Approved) => true,
            (LocalOwnerRecoveryStatuses.Pending, LocalOwnerRecoveryStatuses.Rejected) => true,
            (LocalOwnerRecoveryStatuses.Pending, LocalOwnerRecoveryStatuses.Cancelled) => true,
            (LocalOwnerRecoveryStatuses.Pending, LocalOwnerRecoveryStatuses.Expired) => true,
            (LocalOwnerRecoveryStatuses.Approved, LocalOwnerRecoveryStatuses.Completed) => true,
            (LocalOwnerRecoveryStatuses.Approved, LocalOwnerRecoveryStatuses.Revoked) => true,
            (LocalOwnerRecoveryStatuses.Approved, LocalOwnerRecoveryStatuses.Expired) => true,
            _ => false,
        };
        if (!ok)
            throw new InvalidOperationException($"Cannot move a {from} recovery request to {to}.");
    }

    static void AppendHistory(LocalOwnerRecoveryRequest row, string evt, string? actor, string? outcome, string? reason)
    {
        var items = ParseHistory(row.HistoryJson).ToList();
        items.Add(new LocalOwnerRecoveryHistoryItemDto
        {
            AtUtc = DateTime.UtcNow,
            Event = evt,
            Actor = actor,
            Outcome = outcome,
            Reason = reason,
        });
        row.HistoryJson = JsonSerializer.Serialize(items);
    }

    static IReadOnlyList<LocalOwnerRecoveryHistoryItemDto> ParseHistory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<LocalOwnerRecoveryHistoryItemDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    static LocalOwnerRecoveryListItemDto ToListItem(LocalOwnerRecoveryRequest row) => new()
    {
        Id = row.Id,
        LicenseId = row.LicenseId,
        CustomerId = row.CustomerId,
        InstallationId = row.InstallationId,
        GymCode = row.GymCode,
        GymName = row.GymName,
        Status = row.Status,
        Method = row.Method,
        CreatedAtUtc = row.CreatedAtUtc,
        ExpiresAtUtc = row.ExpiresAtUtc,
        CompletedAtUtc = row.CompletedAtUtc,
        Reference = OwnerRecoveryCodec.FormatOwnerFacingReference(row.Id),
    };

    static LocalOwnerRecoveryDetailDto ToDetail(LocalOwnerRecoveryRequest row, bool includeRecoveryCode)
    {
        var dto = new LocalOwnerRecoveryDetailDto
        {
            Id = row.Id,
            LicenseId = row.LicenseId,
            CustomerId = row.CustomerId,
            InstallationId = row.InstallationId,
            GymCode = row.GymCode,
            GymName = row.GymName,
            Status = row.Status,
            Method = row.Method,
            CreatedAtUtc = row.CreatedAtUtc,
            ExpiresAtUtc = row.ExpiresAtUtc,
            CompletedAtUtc = row.CompletedAtUtc,
            ApprovedAtUtc = row.ApprovedAtUtc,
            RejectedAtUtc = row.RejectedAtUtc,
            RevokedAtUtc = row.RevokedAtUtc,
            DecisionByPlatformUserId = row.DecisionByPlatformUserId,
            DecisionReason = row.DecisionReason,
            Reference = OwnerRecoveryCodec.FormatOwnerFacingReference(row.Id),
            History = ParseHistory(row.HistoryJson),
            RecoveryCode = includeRecoveryCode ? row.ApprovalBlob : null,
        };
        return dto;
    }

    static LocalOwnerRecoveryClientDto ToClient(LocalOwnerRecoveryRequest row, bool includeCode) => new()
    {
        RequestId = row.Id,
        Status = row.Status,
        Method = row.Method,
        Reference = OwnerRecoveryCodec.FormatOwnerFacingReference(row.Id),
        CreatedAtUtc = row.CreatedAtUtc,
        ExpiresAtUtc = row.ExpiresAtUtc,
        RecoveryCode = includeCode ? row.ApprovalBlob : null,
        Message = row.Status switch
        {
            LocalOwnerRecoveryStatuses.Pending => "Waiting for HyMotion support.",
            LocalOwnerRecoveryStatuses.Approved => "Approved. Set a new owner password on this PC.",
            LocalOwnerRecoveryStatuses.Completed => "Owner password was updated on this gym.",
            LocalOwnerRecoveryStatuses.Rejected => "This recovery request was not approved.",
            LocalOwnerRecoveryStatuses.Expired => "This recovery request expired. Start a new one.",
            LocalOwnerRecoveryStatuses.Cancelled => "This recovery request was replaced.",
            LocalOwnerRecoveryStatuses.Revoked => "This recovery approval was revoked.",
            _ => null,
        },
    };

    static string? FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a.Trim() : (!string.IsNullOrWhiteSpace(b) ? b.Trim() : null);
}
