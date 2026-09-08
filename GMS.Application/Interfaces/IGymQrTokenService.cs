namespace GMS.Application.Interfaces;

/// <summary>
/// Mints and validates the short-lived signed token embedded in the gym's displayed QR code —
/// the "physical presence" proof for member and employee QR check-in (GPS is out of scope for
/// this phase; the QR's short lifetime is the only presence signal).
///
/// Stateless (no DB round-trip): HMAC-SHA256 over {gymCode}:{expUnixSeconds}:{nonce}, signed with
/// the same JwtSettings:SecretKey already used for access tokens (reuses existing security
/// infrastructure rather than inventing a second secret/auth mechanism). The token carries only
/// the tenant's public GymCode + an expiry + a random nonce — no user identity, no secrets.
///
/// The SAME token is scanned by many different members/employees while it's valid (it identifies
/// "a screen showing a fresh code for this gym", not one individual) — validation is therefore
/// repeatable within the window, not single-use/consumed.
/// </summary>
public interface IGymQrTokenService
{
    /// <summary>Mints a fresh signed token for this gym. Returns the opaque token string and its UTC expiry.
    /// Pass <paramref name="lifetime"/> to override the default (45s — the middle of the recommended 30-60s range).</summary>
    (string Token, DateTime ExpiresAtUtc) GenerateToken(string gymCode, TimeSpan? lifetime = null);

    /// <summary>
    /// Validates signature + expiry and extracts the embedded gym code.
    /// Does NOT check tenant match against the caller — callers must still compare the
    /// resolved gym code's tenant against their own ambient tenant context.
    /// </summary>
    GymQrTokenValidationResult Validate(string token);
}

public sealed class GymQrTokenValidationResult
{
    public bool IsValid { get; init; }
    public string? GymCode { get; init; }
    public string? Error { get; init; }

    public static GymQrTokenValidationResult Success(string gymCode) => new() { IsValid = true, GymCode = gymCode };
    public static GymQrTokenValidationResult Failure(string error) => new() { IsValid = false, Error = error };
}
