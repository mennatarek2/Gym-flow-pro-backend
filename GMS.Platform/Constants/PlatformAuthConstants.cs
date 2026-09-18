namespace GMS.Platform.Constants;

/// <summary>Platform JWT and auth scheme constants — never reuse tenant audience/policies.</summary>
public static class PlatformAuthConstants
{
    public const string AuthenticationScheme = "PlatformBearer";
    public const string Audience = "gymflow-platform";
    public const string IssuerConfigKey = "JwtSettings:Issuer";
    public const string SecretConfigKey = "JwtSettings:SecretKey";
    /// <summary>Shorter than tenant staff tokens (15m) — leaked platform tokens are high blast-radius.</summary>
    public const int AccessTokenExpirationMinutes = 10;
    public const string RoleClaimType = "platform_role";
    public const string SetupPurpose = "platform-mfa-setup";
}

public static class PlatformRoles
{
    public const string Support = "platform_support";
    public const string Ops = "platform_ops";
    public const string Admin = "platform_admin";

    /// <summary>NOT part of the Support/Ops/Admin hierarchy — a sales rep must never inherit
    /// broader platform access by virtue of some future "OrAbove" chain. Deliberately its own,
    /// narrow policy (PlatformSalesOrAbove) rather than being slotted below Support.</summary>
    public const string Sales = "platform_sales";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Support, Ops, Admin, Sales
    };

    public static bool IsValid(string? role) =>
        !string.IsNullOrWhiteSpace(role) && All.Contains(role.Trim());
}
