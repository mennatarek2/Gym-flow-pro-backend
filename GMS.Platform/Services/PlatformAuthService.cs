namespace GMS.Platform.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class PlatformAuthService : IPlatformAuthService
{
    private readonly PlatformDbContext _db;
    private readonly IPlatformTokenService _tokens;
    private readonly IPlatformMfaSecretStore _mfaStore;
    private readonly IPlatformAuditService _audit;
    private readonly IPasswordHasher<PlatformAdminUser> _passwordHasher;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlatformAuthService> _logger;

    public PlatformAuthService(
        PlatformDbContext db,
        IPlatformTokenService tokens,
        IPlatformMfaSecretStore mfaStore,
        IPlatformAuditService audit,
        IPasswordHasher<PlatformAdminUser> passwordHasher,
        IHostEnvironment env,
        IConfiguration configuration,
        ILogger<PlatformAuthService> logger)
    {
        _db = db;
        _tokens = tokens;
        _mfaStore = mfaStore;
        _audit = audit;
        _passwordHasher = passwordHasher;
        _env = env;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PlatformLoginResult> LoginAsync(PlatformLoginRequest request, string? ipAddress)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _db.PlatformAdminUsers
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null || !user.IsActive)
            return Fail("INVALID_CREDENTIALS", "Invalid email or password.");

        var verify = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty);
        if (verify == PasswordVerificationResult.Failed)
            return Fail("INVALID_CREDENTIALS", "Invalid email or password.");

        // Dev-only: allow password login without authenticator (never enable in Production).
        if (AllowMfaBypass())
        {
            _logger.LogWarning(
                "Platform MFA bypassed for {Email} (Development + PlatformAuth:BypassMfaInDevelopment)",
                email);
            user.LastLoginAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(user.Id, "platform.auth.login_mfa_bypassed", ipAddress: ipAddress);
            return IssueAccessToken(user);
        }

        // MFA is mandatory — never issue an access token until MFA is configured and verified.
        if (!user.MfaEnabled || string.IsNullOrWhiteSpace(user.MfaSecret))
        {
            var rawSecret = PlatformTotp.GenerateSecret();
            user.MfaSecret = _mfaStore.StoreSecret(user.Id, rawSecret);
            await _db.SaveChangesAsync();

            var setupToken = _tokens.GenerateMfaSetupToken(user);
            await _audit.LogAsync(user.Id, "platform.auth.mfa_setup_required", ipAddress: ipAddress);

            return new PlatformLoginResult
            {
                Success = false,
                ErrorCode = "MFA_SETUP_REQUIRED",
                ErrorMessage = "MFA enrollment is required before platform access. Complete setup with an authenticator app.",
                MfaSetupRequired = true,
                SetupToken = setupToken,
                MfaManualKey = rawSecret,
                OtpAuthUri = PlatformTotp.BuildOtpAuthUri(user.Email, rawSecret),
                User = MapUser(user)
            };
        }

        if (string.IsNullOrWhiteSpace(request.MfaCode))
            return Fail("MFA_REQUIRED", "MFA code is required.");

        var secret = _mfaStore.ResolveSecret(user.MfaSecret);
        if (secret == null || !PlatformTotp.Verify(secret, request.MfaCode))
        {
            await _audit.LogAsync(user.Id, "platform.auth.mfa_failed", ipAddress: ipAddress);
            return Fail("MFA_INVALID", "Invalid MFA code.");
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(user.Id, "platform.auth.login", ipAddress: ipAddress);
        return IssueAccessToken(user);
    }

    public async Task<PlatformLoginResult> CompleteMfaSetupAsync(PlatformMfaSetupRequest request, string? ipAddress)
    {
        var userId = _tokens.ValidateMfaSetupToken(request.SetupToken ?? string.Empty);
        if (userId == null)
            return Fail("SETUP_TOKEN_INVALID", "MFA setup token is invalid or expired.");

        var user = await _db.PlatformAdminUsers.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user == null || !user.IsActive)
            return Fail("USER_NOT_FOUND", "Platform user not found.");

        var secret = _mfaStore.ResolveSecret(user.MfaSecret);
        if (secret == null)
            return Fail("MFA_SECRET_MISSING", "MFA secret is missing — restart login to regenerate.");

        if (!PlatformTotp.Verify(secret, request.MfaCode ?? string.Empty))
            return Fail("MFA_INVALID", "Invalid MFA code — scan the QR again and retry.");

        user.MfaEnabled = true;
        user.LastLoginAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(user.Id, "platform.auth.mfa_enabled", ipAddress: ipAddress);
        return IssueAccessToken(user);
    }

    private bool AllowMfaBypass() =>
        _env.IsDevelopment()
        && _configuration.GetValue("PlatformAuth:BypassMfaInDevelopment", false);

    private PlatformLoginResult IssueAccessToken(PlatformAdminUser user) => new()
    {
        Success = true,
        AccessToken = _tokens.GenerateAccessToken(user),
        ExpiresInSeconds = PlatformAuthConstants.AccessTokenExpirationMinutes * 60,
        User = MapUser(user)
    };

    private static PlatformLoginResult Fail(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message
    };

    private static PlatformAdminDto MapUser(PlatformAdminUser user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        Role = user.Role,
        MfaEnabled = user.MfaEnabled
    };
}
