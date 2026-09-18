namespace GMS.Infrastructure.Services;

using System.Net.Http.Json;
using GMS.Core.Configuration;
using GMS.Core.Interfaces;
using GMS.Core.Licensing;
using GMS.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// See ILocalLicenseClientService. Grace-period model: every successful /activate or /validate
/// call refreshes the stored payload's IssuedAtUtc (server clock, embedded at signing time) -
/// GetCurrentStatus compares that against the LOCAL clock, which is inherently imperfect (rule:
/// "do not make destructive decisions based solely on local clock changes"). That is why
/// exceeding the grace period only ever sets GracePeriodExceeded=true (a soft signal the caller
/// can choose how to act on) rather than deleting the stored license or hard-blocking the
/// process - a legitimate customer whose PC clock is merely wrong, or who is genuinely offline
/// for a while, must never be silently locked out of their own gym software.
/// </summary>
public class LocalLicenseClientService : ILocalLicenseClientService
{
    /// <summary>How long a Local install runs on a locally-cached license before it needs to
    /// reach the license server again. Generous on purpose (rule 19: normal operation is
    /// local/offline, periodic validation is "online when available").</summary>
    public const int GracePeriodDays = 14;

    private readonly LocalLicenseStore _store;
    private readonly ILicenseVerificationService _verification;
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalLicenseClientService> _logger;

    public LocalLicenseClientService(
        LocalLicenseStore store,
        ILicenseVerificationService verification,
        HttpClient http,
        IConfiguration configuration,
        ILogger<LocalLicenseClientService> logger)
    {
        _store = store;
        _verification = verification;
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public string GetOrCreateInstallationId() => _store.GetOrCreateInstallationId();

    public string? TryGetInstallationId() => _store.TryGetInstallationId();

    public LocalLicensePayload? GetStoredLicense() => _store.GetLicense();

    public async Task<LocalLicenseActivationOutcome> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        var installationId = GetOrCreateInstallationId();
        var fingerprint = _store.GetMachineFingerprint();

        var baseUrl = _configuration["LicenseServer:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Fail("not_configured", "This install does not have a license server configured.");

        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/api/local-license/activate";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
            request.Content = JsonContent.Create(new { licenseKey, installationId, machineFingerprint = fingerprint });
            var response = await _http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await TryReadActivationErrorAsync(response, cancellationToken);
                return Fail(error?.error ?? "activation_failed", error?.message ?? "Activation was rejected by the license server.");
            }

            LocalLicensePayload? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<LocalLicensePayload>(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "License activation response was not a signed payload.");
                await TryReleaseQuietAsync(licenseKey, cancellationToken);
                return Fail("empty_response", "The license server could not be reached. Check the connection and try again.");
            }
            if (payload == null)
            {
                await TryReleaseQuietAsync(licenseKey, cancellationToken);
                return Fail("empty_response", "License server returned an empty response.");
            }

            // Never persist a payload that doesn't verify - a compromised/misconfigured server
            // response must not become "this install is now licensed".
            if (!_verification.Verify(payload))
            {
                _logger.LogError("Activation response failed signature verification - refusing to store it.");
                await TryReleaseQuietAsync(licenseKey, cancellationToken);
                return Fail("signature_invalid", "The license server's response could not be verified. Contact support.");
            }

            try
            {
                _store.SaveLicense(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Activated on the license server but could not save the license file.");
                await TryReleaseQuietAsync(licenseKey, cancellationToken);
                return Fail("store_failed", "The license was activated but could not be saved on this PC. The slot was released. Try again.");
            }

            return new LocalLicenseActivationOutcome { Success = true };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not reach the license server for activation.");
            return Fail("network_error", "Could not reach the license server. Check your internet connection and try again.");
        }
    }

    public async Task<LocalLicenseActivationOutcome> CheckAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        var installationId = GetOrCreateInstallationId();
        var baseUrl = _configuration["LicenseServer:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Fail("not_configured", "This install does not have a license server configured.");

        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/api/local-license/check";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
            request.Content = JsonContent.Create(new { licenseKey, installationId });
            var response = await _http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await TryReadActivationErrorAsync(response, cancellationToken);
                return Fail(error?.error ?? "activation_failed", error?.message ?? "This license cannot be used on this installation.");
            }

            return new LocalLicenseActivationOutcome { Success = true };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not reach the license server for a license check.");
            return Fail("network_error", "Could not reach the license server. Check your internet connection and try again.");
        }
    }

    public async Task<LocalLicenseActivationOutcome> ReleaseAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        var installationId = GetOrCreateInstallationId();
        var baseUrl = _configuration["LicenseServer:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Fail("not_configured", "This install does not have a license server configured.");

        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/api/local-license/release";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
            request.Content = JsonContent.Create(new { licenseKey, installationId });
            var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await TryReadActivationErrorAsync(response, cancellationToken);
                return Fail(error?.error ?? "release_failed", error?.message ?? "Could not release this installation's license slot.");
            }

            return new LocalLicenseActivationOutcome { Success = true };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not reach the license server to release a device slot.");
            return Fail("network_error", "Could not reach the license server to release the license slot.");
        }
    }

    async Task TryReleaseQuietAsync(string licenseKey, CancellationToken cancellationToken)
    {
        try
        {
            await ReleaseAsync(licenseKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release license slot after a local persist failure.");
        }
    }

    public LocalLicenseStatus GetCurrentStatus()
    {
        var installationId = GetOrCreateInstallationId();
        var payload = _store.GetLicense();

        if (payload == null)
            return new LocalLicenseStatus { HasLicense = false, InstallationId = installationId };

        var signatureValid = _verification.Verify(payload);
        var age = DateTime.UtcNow - payload.IssuedAtUtc;
        var withinGrace = age <= TimeSpan.FromDays(GracePeriodDays);

        return new LocalLicenseStatus
        {
            HasLicense = true,
            SignatureValid = signatureValid,
            WithinGracePeriod = withinGrace,
            GracePeriodExceeded = !withinGrace,
            LicenseKey = payload.LicenseKey,
            Edition = payload.Edition,
            LastConfirmedAtUtc = payload.IssuedAtUtc,
            InstallationId = installationId,
        };
    }

    public Task<bool> TryRevalidateAsync(CancellationToken cancellationToken = default) =>
        TryRevalidateAsync(null, null, cancellationToken);

    public async Task<bool> TryRevalidateAsync(string? gymCode, string? gymName, CancellationToken cancellationToken = default)
    {
        var payload = _store.GetLicense();
        if (payload == null) return false;

        var baseUrl = _configuration["LicenseServer:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;

        try
        {
            var validateUrl = $"{baseUrl.TrimEnd('/')}/api/local-license/validate";
            using var request = new HttpRequestMessage(HttpMethod.Post, validateUrl);
            request.Headers.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
            request.Content = JsonContent.Create(new
            {
                licenseKey = payload.LicenseKey,
                installationId = GetOrCreateInstallationId(),
                gymCode,
                gymName,
                appVersion = typeof(LocalLicenseClientService).Assembly.GetName().Version?.ToString(),
            });
            var response = await _http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // A real rejection (revoked/suspended/unbound) - log it, but still do not delete
                // the local copy or crash the app; GetCurrentStatus's grace period is what
                // eventually reflects this to the rest of the app if it keeps failing.
                _logger.LogWarning("License validation was rejected by the server (status {Status}).", response.StatusCode);
                return false;
            }

            var refreshed = await response.Content.ReadFromJsonAsync<LocalLicensePayload>(cancellationToken: cancellationToken);
            if (refreshed == null || !_verification.Verify(refreshed))
                return false;

            _store.SaveLicense(refreshed);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogInformation(ex, "License revalidation could not reach the server - continuing on the cached license.");
            return false;
        }
    }

    public async Task ReportLifecycleEventAsync(
        string licenseKey,
        Guid operationId,
        string eventType,
        string? gymCode = null,
        string? gymName = null,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _configuration["LicenseServer:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(licenseKey))
            return;

        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/api/local-license/lifecycle-event";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
            request.Content = JsonContent.Create(new
            {
                licenseKey,
                installationId = GetOrCreateInstallationId(),
                operationId,
                eventType,
                gymCode,
                gymName,
                message,
                appVersion = typeof(LocalLicenseClientService).Assembly.GetName().Version?.ToString(),
                idempotencyKey = $"{operationId:N}:{eventType}",
            });
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Lifecycle event {EventType} returned {Status}", eventType, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lifecycle event {EventType} could not reach the license server.", eventType);
        }
    }

    public async Task<LocalDeskFeedbackReportOutcome> ReportDeskFeedbackAsync(
        LocalDeskFeedbackReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _configuration["LicenseServer:BaseUrl"];
        var license = _store.GetLicense();
        var licenseKey = license?.LicenseKey;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new LocalDeskFeedbackReportOutcome
            {
                Success = false,
                ErrorCode = "not_configured",
                ErrorMessage = "License server is not configured.",
            };
        }

        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return new LocalDeskFeedbackReportOutcome
            {
                Success = false,
                ErrorCode = "no_license",
                ErrorMessage = "No local license is available to authenticate feedback.",
            };
        }

        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/api/local-license/desk-feedback";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
            httpRequest.Content = JsonContent.Create(new
            {
                licenseKey,
                installationId = GetOrCreateInstallationId(),
                tenantId = request.TenantId,
                senderUserId = request.SenderUserId,
                senderRole = request.SenderRole,
                senderEmail = request.SenderEmail,
                senderDisplayName = request.SenderDisplayName,
                gymCode = request.GymCode,
                gymName = request.GymName,
                category = request.Category,
                subject = request.Subject,
                message = request.Message,
                appVersion = request.AppVersion
                    ?? typeof(LocalLicenseClientService).Assembly.GetName().Version?.ToString(),
                pageUrl = request.PageUrl,
                clientRequestId = request.ClientRequestId,
            });
            using var response = await _http.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await TryReadActivationErrorAsync(response, cancellationToken);
                _logger.LogWarning(
                    "Desk feedback forward returned {Status}: {Message}",
                    (int)response.StatusCode,
                    body?.message ?? body?.error);
                return new LocalDeskFeedbackReportOutcome
                {
                    Success = false,
                    ErrorCode = body?.error ?? "rejected",
                    ErrorMessage = body?.message ?? $"License server returned {(int)response.StatusCode}.",
                };
            }

            var dto = await response.Content.ReadFromJsonAsync<DeskFeedbackReportBody>(
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);
            return new LocalDeskFeedbackReportOutcome
            {
                Success = true,
                Id = dto?.Id,
                AlreadySubmitted = dto?.AlreadySubmitted ?? false,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Desk feedback could not reach the license server.");
            return new LocalDeskFeedbackReportOutcome
            {
                Success = false,
                ErrorCode = "unreachable",
                ErrorMessage = "Could not reach HyMotion. Check the internet connection and try again.",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Desk feedback forward failed unexpectedly.");
            return new LocalDeskFeedbackReportOutcome
            {
                Success = false,
                ErrorCode = "error",
                ErrorMessage = "Could not deliver feedback to HyMotion.",
            };
        }
    }

    private static async Task<ActivationErrorBody?> TryReadActivationErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ActivationErrorBody>(cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static LocalLicenseActivationOutcome Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message };

    private class ActivationErrorBody
    {
        public string? error { get; set; }
        public string? message { get; set; }
    }

    private class DeskFeedbackReportBody
    {
        public Guid Id { get; set; }
        public bool AlreadySubmitted { get; set; }
    }
}
