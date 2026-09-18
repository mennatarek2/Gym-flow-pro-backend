namespace GMS.Infrastructure.Configuration;

/// <summary>
/// Local Edition per-install secret bootstrap. Ensures configuration keys such as
/// JwtSettings:SecretKey or EncryptionKey have a value on a fresh Local install by generating one
/// the first time it's missing, persisting it to disk, and returning the same value on every
/// subsequent call — never regenerating once a secret exists. Never used for SaaS.
///
/// This is a startup-time bootstrap, not a runtime secret store: callers feed the returned values
/// into <see cref="Microsoft.Extensions.Configuration.IConfigurationBuilder"/> once, before the
/// host is built, so every existing consumer (AesEncryptionService, the JWT bearer setup in
/// Program.cs, activation pepper options, ...) keeps reading from IConfiguration exactly as today
/// — no second secret-access pathway is introduced.
/// </summary>
public interface ILocalSecretProvider
{
    /// <summary>
    /// For each key in <paramref name="requiredKeys"/> that has no non-empty value in
    /// <paramref name="existingValues"/> (i.e. not already supplied via appsettings/user-secrets/
    /// environment variables), returns a value — reused from the persisted store if one already
    /// exists there, otherwise freshly generated and persisted. Keys already present in
    /// <paramref name="existingValues"/> are left untouched and are not present in the result, so
    /// an explicit operator override always wins.
    /// </summary>
    IReadOnlyDictionary<string, string> EnsureSecrets(
        IEnumerable<string> requiredKeys, IReadOnlyDictionary<string, string?> existingValues);
}
