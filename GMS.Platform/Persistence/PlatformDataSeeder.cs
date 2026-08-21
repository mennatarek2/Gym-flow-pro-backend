namespace GMS.Platform.Persistence;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GMS.Platform.Constants;
using GMS.Platform.Entities;

public class PlatformDataSeeder
{
    private readonly PlatformDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IPasswordHasher<PlatformAdminUser> _passwordHasher;
    private readonly ILogger<PlatformDataSeeder> _logger;

    public PlatformDataSeeder(
        PlatformDbContext db,
        IConfiguration configuration,
        IPasswordHasher<PlatformAdminUser> passwordHasher,
        ILogger<PlatformDataSeeder> logger)
    {
        _db = db;
        _configuration = configuration;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    /// <summary>
    /// Ensures schema exists and seeds the first platform_admin when PlatformSeed is configured.
    /// MFA is intentionally left disabled so the forced-setup flow runs on first login.
    /// </summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await _db.Database.MigrateWithSqlImportBaselineAsync(
            _logger,
            PlatformServiceExtensions.Schema,
            PlatformServiceExtensions.MigrationsHistoryTable,
            ct);

        await EnsureTierFeatureMapSeededAsync(ct);

        var email = (_configuration["PlatformSeed:Email"] ?? string.Empty).Trim().ToLowerInvariant();
        var password = _configuration["PlatformSeed:Password"];
        var fullName = _configuration["PlatformSeed:FullName"] ?? "Platform Admin";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogInformation(
                "PlatformSeed:Email/Password not configured — skipping platform_admin seed.");
            return;
        }

        var existing = await _db.PlatformAdminUsers.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (existing != null)
        {
            // Dev convenience: keep PlatformSeed password in sync until MFA is enrolled.
            // Once MFA is on, never overwrite — that would silently lock ops out of a real account.
            var syncPassword = _configuration.GetValue("PlatformSeed:SyncPasswordIfMfaDisabled", false);
            if (syncPassword && !existing.MfaEnabled)
            {
                existing.PasswordHash = _passwordHasher.HashPassword(existing, password);
                existing.IsActive = true;
                existing.FullName = fullName;
                await _db.SaveChangesAsync(ct);
                _logger.LogWarning(
                    "Synced platform_admin {Email} password from PlatformSeed (MFA still disabled).",
                    email);
            }
            else
            {
                _logger.LogInformation("Platform admin {Email} already exists — seed skipped.", email);
            }

            return;
        }

        var user = new PlatformAdminUser
        {
            Email = email,
            FullName = fullName,
            Role = PlatformRoles.Admin,
            MfaEnabled = false,
            MfaSecret = null,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        _db.PlatformAdminUsers.Add(user);
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Seeded platform_admin {Email}. MFA is NOT enabled — first login will force MFA setup. Change PlatformSeed:Password after enrollment.",
            email);
    }

    private async Task EnsureTierFeatureMapSeededAsync(CancellationToken ct)
    {
        var expected = TierFeatureMapSeed.BuildAll();
        var existingKeys = await _db.TierFeatureMaps
            .Select(m => new { m.Tier, m.FeatureKey })
            .ToListAsync(ct);
        var existingSet = existingKeys
            .Select(k => $"{k.Tier}|{k.FeatureKey}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = expected
            .Where(r => !existingSet.Contains($"{r.Tier}|{r.FeatureKey}"))
            .ToList();

        if (missing.Count == 0)
            return;

        _db.TierFeatureMaps.AddRange(missing);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} tier_feature_map row(s).", missing.Count);
    }
}
