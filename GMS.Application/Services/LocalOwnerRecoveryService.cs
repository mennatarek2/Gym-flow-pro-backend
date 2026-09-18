namespace GMS.Application.Services;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Auth;
using GMS.Application.Interfaces;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Core.Licensing;
using GMS.Infrastructure.Configuration;
using GMS.Platform.DTOs;

public class LocalOwnerRecoveryService : ILocalOwnerRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ILocalFirstRunService _firstRun;
    private readonly ILocalLicenseClientService _license;
    private readonly ILicenseVerificationService _verification;
    private readonly LocalOwnerRecoveryStore _store;
    private readonly LocalDeviceSessionStore _deviceSession;
    private readonly IAdminService _admin;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuditService _audit;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalOwnerRecoveryService> _logger;

    public LocalOwnerRecoveryService(
        ILocalFirstRunService firstRun,
        ILocalLicenseClientService license,
        ILicenseVerificationService verification,
        LocalOwnerRecoveryStore store,
        LocalDeviceSessionStore deviceSession,
        IAdminService admin,
        UserManager<ApplicationUser> users,
        IAuditService audit,
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogger<LocalOwnerRecoveryService> logger)
    {
        _firstRun = firstRun;
        _license = license;
        _verification = verification;
        _store = store;
        _deviceSession = deviceSession;
        _admin = admin;
        _users = users;
        _audit = audit;
        _httpFactory = httpFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<OwnerRecoveryContextResponse> GetContextAsync(CancellationToken cancellationToken = default)
    {
        var status = await _firstRun.GetStatusAsync(cancellationToken);
        return new OwnerRecoveryContextResponse
        {
            Available = !string.IsNullOrWhiteSpace(status.GymCode),
            GymCode = status.GymCode,
            GymName = status.GymName,
            HasOwner = status.OwnerMissing != true && status.TenantId != null,
            HasLicense = _license.GetStoredLicense() != null,
        };
    }

    public async Task<Result<OwnerRecoveryStatusResponse>> StartAsync(CancellationToken cancellationToken = default)
    {
        var status = await _firstRun.GetStatusAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(status.GymCode))
            return Result<OwnerRecoveryStatusResponse>.Failure("This PC is not bound to a gym.");

        var installationId = _license.TryGetInstallationId();
        if (string.IsNullOrWhiteSpace(installationId))
            return Result<OwnerRecoveryStatusResponse>.Failure("This PC does not have a local installation identity.");

        var existing = _store.Load();
        if (existing != null
            && existing.Status == "approved"
            && existing.ApprovalToken != null
            && string.Equals(existing.InstallationId, installationId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.GymCode, status.GymCode, StringComparison.OrdinalIgnoreCase)
            && (existing.ExpiresAtUtc is not DateTime exp || !OwnerRecoveryCodec.IsExpired(exp, DateTime.UtcNow)))
        {
            return Result<OwnerRecoveryStatusResponse>.Success(ToStatus(existing, "An approved recovery is already waiting on this PC."));
        }

        var requestId = Guid.NewGuid();
        var nonce = OwnerRecoveryCodec.NewNonce();
        var challenge = new OwnerRecoveryChallenge
        {
            RequestId = requestId,
            InstallationId = installationId,
            GymCode = status.GymCode,
            GymName = status.GymName,
            Nonce = nonce,
            IssuedAtUtc = DateTime.UtcNow,
        };
        var challengeText = OwnerRecoveryCodec.FormatChallenge(challenge);

        var content = new LocalOwnerRecoveryFileContent
        {
            RequestId = requestId,
            InstallationId = installationId,
            GymCode = status.GymCode,
            GymName = status.GymName,
            Nonce = nonce,
            Status = "pending",
            Method = "offline",
            CreatedAtUtc = DateTime.UtcNow,
            ChallengeText = challengeText,
        };

        var payload = _license.GetStoredLicense();
        if (payload != null)
        {
            var remote = await PostPlatformAsync("owner-recovery/request", new LocalOwnerRecoveryAuthRequest
            {
                LicenseKey = payload.LicenseKey,
                InstallationId = installationId,
                RequestId = requestId,
                GymCode = status.GymCode,
                GymName = status.GymName,
                Nonce = nonce,
            }, cancellationToken);
            if (remote != null)
            {
                content.OnlineSubmitted = true;
                content.Method = remote.Method;
                content.Status = remote.Status;
                content.ExpiresAtUtc = remote.ExpiresAtUtc;
                if (!string.IsNullOrWhiteSpace(remote.RecoveryCode))
                    ApplyApproval(content, remote.RecoveryCode);
            }
        }

        _store.Save(content);
        await _audit.LogAsync(
            "owner.recovery_requested",
            "OwnerRecovery",
            requestId,
            after: new { requestId, gymCode = status.GymCode, installationId, online = content.OnlineSubmitted },
            tenantIdOverride: status.TenantId);

        return Result<OwnerRecoveryStatusResponse>.Success(ToStatus(content, content.OnlineSubmitted
            ? "Recovery request sent. HyMotion support will review it."
            : "Recovery request saved on this PC. Share the request with HyMotion support."));
    }

    public async Task<OwnerRecoveryStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var content = _store.Load();
        if (content == null)
            return new OwnerRecoveryStatusResponse { Status = "none" };

        if (content.Status == "approved" && content.ExpiresAtUtc is DateTime exp && OwnerRecoveryCodec.IsExpired(exp, DateTime.UtcNow))
        {
            content.Status = "expired";
            content.ApprovalToken = null;
            _store.Save(content);
        }

        if (content.Status is "pending" or "approved" && !content.PlatformCompletionRecorded)
            await TrySyncPlatformAsync(content, cancellationToken);

        if (content.PasswordResetAtUtc != null && !content.PlatformCompletionRecorded)
            await TryRecordCompletionAsync(content, cancellationToken);

        return ToStatus(content, null);
    }

    public async Task<Result<OwnerRecoveryStatusResponse>> SubmitCodeAsync(string recoveryCode, CancellationToken cancellationToken = default)
    {
        var content = _store.Load();
        if (content == null)
            return Result<OwnerRecoveryStatusResponse>.Failure("Start a recovery request on this PC first.");

        var applied = ApplyApproval(content, recoveryCode);
        if (!applied.IsSuccess)
            return Result<OwnerRecoveryStatusResponse>.Failure(applied.Error ?? "This recovery code is not valid for this gym.");

        _store.Save(content);
        await _audit.LogAsync(
            "owner.recovery_code_accepted",
            "OwnerRecovery",
            content.RequestId,
            after: new { content.RequestId, content.GymCode, content.InstallationId });
        return Result<OwnerRecoveryStatusResponse>.Success(ToStatus(content, "Recovery approved. Set a new owner password."));
    }

    public async Task<Result<OwnerRecoveryStatusResponse>> CompleteAsync(
        CompleteOwnerRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var password = request.NewPassword ?? string.Empty;
        var confirm = request.ConfirmPassword ?? string.Empty;
        if (password.Length < 8)
            return Result<OwnerRecoveryStatusResponse>.Failure("Password must be at least 8 characters.");
        if (!string.Equals(password, confirm, StringComparison.Ordinal))
            return Result<OwnerRecoveryStatusResponse>.Failure("Password confirmation does not match.");

        var content = _store.Load();
        if (content == null)
            return Result<OwnerRecoveryStatusResponse>.Failure("Start a recovery request on this PC first.");

        if (content.PasswordResetAtUtc != null)
        {
            await TryRecordCompletionAsync(content, cancellationToken);
            return Result<OwnerRecoveryStatusResponse>.Success(ToStatus(content, "Owner password was already updated."));
        }

        if (content.Status != "approved" || string.IsNullOrWhiteSpace(content.ApprovalToken))
            return Result<OwnerRecoveryStatusResponse>.Failure("This recovery request is not ready for a password change.");
        if (content.ExpiresAtUtc is DateTime exp && OwnerRecoveryCodec.IsExpired(exp, DateTime.UtcNow))
        {
            content.Status = "expired";
            content.ApprovalToken = null;
            _store.Save(content);
            return Result<OwnerRecoveryStatusResponse>.Failure("This recovery request expired. Start a new one.");
        }

        var live = await _firstRun.GetStatusAsync(cancellationToken);
        if (!string.Equals(live.GymCode, content.GymCode, StringComparison.OrdinalIgnoreCase)
            || live.TenantId is not Guid tenantId)
            return Result<OwnerRecoveryStatusResponse>.Failure("This recovery request does not match this gym.");

        var installationId = _license.TryGetInstallationId();
        if (!string.Equals(installationId, content.InstallationId, StringComparison.OrdinalIgnoreCase))
            return Result<OwnerRecoveryStatusResponse>.Failure("This recovery request does not match this installation.");

        var bound = ApplyApproval(content, content.ApprovalToken);
        if (!bound.IsSuccess)
            return Result<OwnerRecoveryStatusResponse>.Failure(bound.Error ?? "This recovery approval is not valid.");

        var owner = await FindOwnerAsync(tenantId);
        if (owner == null)
            return Result<OwnerRecoveryStatusResponse>.Failure("Owner recovery cannot be completed on this PC. Contact HyMotion support.");

        var reset = await _admin.ResetStaffPasswordAsync(tenantId, owner.Id, password, allowOwner: true);
        if (!reset.IsSuccess)
            return Result<OwnerRecoveryStatusResponse>.Failure("The new password was not accepted. Use a stronger password and try again.");

        content.PasswordResetAtUtc = DateTime.UtcNow;
        content.Status = "completed";
        content.ApprovalToken = null;
        _deviceSession.Clear();
        _store.Save(content);

        await _audit.LogAsync(
            "owner.recovery_completed",
            "OwnerRecovery",
            content.RequestId,
            after: new { content.RequestId, content.GymCode, content.InstallationId, ownerUserId = owner.Id },
            tenantIdOverride: tenantId);

        await TryRecordCompletionAsync(content, cancellationToken);
        return Result<OwnerRecoveryStatusResponse>.Success(ToStatus(content, "Owner password updated. Sign in with the new password."));
    }

    public async Task<OwnerRecoveryStatusResponse> CancelAsync(CancellationToken cancellationToken = default)
    {
        var content = _store.Load();
        if (content == null)
            return new OwnerRecoveryStatusResponse { Status = "none" };
        if (content.Status is "completed")
            return ToStatus(content, null);
        content.Status = "cancelled";
        content.ApprovalToken = null;
        _store.Save(content);
        await Task.CompletedTask;
        return ToStatus(content, "Recovery request cancelled.");
    }

    Result ApplyApproval(LocalOwnerRecoveryFileContent content, string? recoveryCode)
    {
        if (!OwnerRecoveryCodec.TryParseApproval(recoveryCode, out var payload, out var signature))
            return Result.Failure("This recovery code is not valid for this gym.");
        if (!_verification.Verify(OwnerRecoveryCanonicalizer.Build(payload), signature))
            return Result.Failure("This recovery code is not valid for this gym.");
        if (!string.Equals(payload.InstallationId, content.InstallationId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(payload.GymCode, content.GymCode, StringComparison.OrdinalIgnoreCase)
            || payload.RequestId != content.RequestId
            || !string.Equals(payload.Nonce, content.Nonce, StringComparison.Ordinal))
            return Result.Failure("This recovery code is not valid for this gym.");
        if (OwnerRecoveryCodec.IsExpired(payload.ExpiresAtUtc, DateTime.UtcNow))
            return Result.Failure("This recovery request expired. Start a new one.");
        if (content.ApprovalJti is Guid used && used != payload.Jti && content.PasswordResetAtUtc != null)
            return Result.Failure("This recovery code has already been used.");

        content.Status = "approved";
        content.ApprovalJti = payload.Jti;
        content.ApprovalToken = recoveryCode;
        content.ExpiresAtUtc = payload.ExpiresAtUtc;
        content.Method = content.OnlineSubmitted ? "online" : "offline";
        return Result.Success();
    }

    async Task TrySyncPlatformAsync(LocalOwnerRecoveryFileContent content, CancellationToken cancellationToken)
    {
        var payload = _license.GetStoredLicense();
        if (payload == null) return;
        var remote = await PostPlatformAsync("owner-recovery/poll", new LocalOwnerRecoveryAuthRequest
        {
            LicenseKey = payload.LicenseKey,
            InstallationId = content.InstallationId,
            RequestId = content.RequestId,
            GymCode = content.GymCode,
            Nonce = content.Nonce,
        }, cancellationToken);
        if (remote == null) return;
        content.OnlineSubmitted = true;
        if (!string.IsNullOrWhiteSpace(remote.RecoveryCode) && content.PasswordResetAtUtc == null)
            ApplyApproval(content, remote.RecoveryCode);
        else if (remote.Status is "rejected" or "expired" or "revoked" or "cancelled" or "completed"
                 && content.PasswordResetAtUtc == null)
        {
            content.Status = remote.Status;
            if (content.Status != "approved") content.ApprovalToken = null;
        }
        _store.Save(content);
    }

    async Task TryRecordCompletionAsync(LocalOwnerRecoveryFileContent content, CancellationToken cancellationToken)
    {
        if (content.PlatformCompletionRecorded || content.PasswordResetAtUtc == null) return;
        var payload = _license.GetStoredLicense();
        if (payload == null) return;
        var remote = await PostPlatformAsync("owner-recovery/complete", new LocalOwnerRecoveryAuthRequest
        {
            LicenseKey = payload.LicenseKey,
            InstallationId = content.InstallationId,
            RequestId = content.RequestId,
            GymCode = content.GymCode,
            Nonce = content.Nonce,
            Jti = content.ApprovalJti,
        }, cancellationToken);
        if (remote == null) return;
        content.PlatformCompletionRecorded = true;
        content.Status = "completed";
        content.ApprovalToken = null;
        _store.Save(content);
    }

    async Task<LocalOwnerRecoveryClientDto?> PostPlatformAsync(
        string path,
        LocalOwnerRecoveryAuthRequest body,
        CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["LicenseServer:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        try
        {
            var client = _httpFactory.CreateClient("license-server");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/local-license/{path}");
            request.Headers.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
            request.Content = JsonContent.Create(body);
            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<LocalOwnerRecoveryClientDto>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogInformation("Platform owner recovery {Path} unreachable; continuing offline.", path);
            return null;
        }
    }

    async Task<ApplicationUser?> FindOwnerAsync(Guid tenantId)
    {
        var owners = await _users.GetUsersInRoleAsync("Owner");
        var matches = owners.Where(u => u.TenantId == tenantId && u.IsActive).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    static OwnerRecoveryStatusResponse ToStatus(LocalOwnerRecoveryFileContent content, string? message) => new()
    {
        RequestId = content.RequestId,
        Status = content.Status,
        Method = content.Method,
        Reference = OwnerRecoveryCodec.FormatOwnerFacingReference(content.RequestId),
        GymCode = content.GymCode,
        GymName = content.GymName,
        CreatedAtUtc = content.CreatedAtUtc,
        ExpiresAtUtc = content.ExpiresAtUtc,
        OnlineSubmitted = content.OnlineSubmitted,
        ReadyForPassword = content.Status == "approved" && !string.IsNullOrWhiteSpace(content.ApprovalToken) && content.PasswordResetAtUtc == null,
        ChallengeText = content.Status is "pending" or "approved" ? content.ChallengeText : null,
        Message = message,
    };
}
