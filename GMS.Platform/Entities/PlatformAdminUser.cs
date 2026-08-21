namespace GMS.Platform.Entities;

/// <summary>
/// Platform-plane admin identity — entirely separate from AspNetUsers / tenant staff.
/// </summary>
public class PlatformAdminUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>platform_support | platform_ops | platform_admin</summary>
    public string Role { get; set; } = "platform_support";

    public bool MfaEnabled { get; set; }

    /// <summary>
    /// Key Vault reference (e.g. kv://gymflow-platform-mfa-{id}) or local-dev encrypted payload (local:...).
    /// Never store raw TOTP secrets in plaintext.
    /// </summary>
    public string? MfaSecret { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
