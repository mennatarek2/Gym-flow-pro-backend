namespace GMS.Core.Licensing;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Compact, copy-pasteable encodings for the owner-facing recovery challenge (unsigned) and
/// the operator-issued approval (signed). Local never holds Platform private keys.
/// </summary>
public static class OwnerRecoveryCodec
{
    public const string ChallengePrefix = "HYMR1.";
    public const string ApprovalPrefix = "HYMA1.";
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string FormatChallenge(OwnerRecoveryChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var json = JsonSerializer.Serialize(challenge, JsonOptions);
        return ChallengePrefix + ToBase64Url(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryParseChallenge(string? text, out OwnerRecoveryChallenge challenge)
    {
        challenge = new OwnerRecoveryChallenge();
        if (string.IsNullOrWhiteSpace(text)) return false;
        var raw = text.Trim();
        var token = ExtractToken(raw, ChallengePrefix);
        if (token == null) return false;
        try
        {
            var json = Encoding.UTF8.GetString(FromBase64Url(token));
            var parsed = JsonSerializer.Deserialize<OwnerRecoveryChallenge>(json, JsonOptions);
            if (parsed == null) return false;
            if (!string.Equals(parsed.Purpose, OwnerRecoveryChallenge.PurposeValue, StringComparison.Ordinal))
                return false;
            if (parsed.RequestId == Guid.Empty
                || string.IsNullOrWhiteSpace(parsed.InstallationId)
                || string.IsNullOrWhiteSpace(parsed.GymCode)
                || string.IsNullOrWhiteSpace(parsed.Nonce))
                return false;
            challenge = parsed;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string FormatApproval(OwnerRecoveryPayload payload, string base64Signature)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(base64Signature))
            throw new ArgumentException("Signature is required.", nameof(base64Signature));
        var body = ToBase64Url(OwnerRecoveryCanonicalizer.Build(payload));
        var sig = ToBase64Url(Convert.FromBase64String(base64Signature.Trim()));
        return ApprovalPrefix + body + "." + sig;
    }

    public static bool TryParseApproval(string? text, out OwnerRecoveryPayload payload, out string base64Signature)
    {
        payload = new OwnerRecoveryPayload();
        base64Signature = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var raw = ExtractToken(text.Trim(), ApprovalPrefix);
        if (raw == null) return false;
        var parts = raw.Split('.', 2, StringSplitOptions.None);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            return false;
        try
        {
            var canonical = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
            var fields = canonical.Split('|');
            if (fields.Length != 7) return false;
            payload = new OwnerRecoveryPayload
            {
                Purpose = fields[0],
                RequestId = Guid.Parse(fields[1]),
                InstallationId = fields[2],
                GymCode = fields[3],
                Nonce = fields[4],
                Jti = Guid.Parse(fields[5]),
                ExpiresAtUtc = DateTime.Parse(fields[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            };
            if (payload.ExpiresAtUtc.Kind == DateTimeKind.Unspecified)
                payload.ExpiresAtUtc = DateTime.SpecifyKind(payload.ExpiresAtUtc, DateTimeKind.Utc);
            else if (payload.ExpiresAtUtc.Kind == DateTimeKind.Local)
                payload.ExpiresAtUtc = payload.ExpiresAtUtc.ToUniversalTime();
            base64Signature = Convert.ToBase64String(FromBase64Url(parts[1]));
            return string.Equals(payload.Purpose, OwnerRecoveryPayload.PurposeValue, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            payload = new OwnerRecoveryPayload();
            base64Signature = string.Empty;
            return false;
        }
    }

    public static bool IsExpired(DateTime expiresAtUtc, DateTime utcNow) =>
        utcNow > expiresAtUtc + ClockSkew;

    public static bool IsNotYetValid(DateTime expiresAtUtc, DateTime utcNow)
    {
        // Approvals are valid from issue until expiry. A far-future expiry is fine; a clock
        // that is wildly ahead of expiry+skew is treated as expired by IsExpired.
        _ = expiresAtUtc;
        _ = utcNow;
        return false;
    }

    public static string FormatOwnerFacingReference(Guid requestId) =>
        requestId.ToString("N")[..8].ToUpperInvariant();

    public static string NewNonce() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    static string? ExtractToken(string text, string prefix)
    {
        var idx = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var token = text[(idx + prefix.Length)..].Trim();
        var end = token.IndexOfAny([' ', '\r', '\n', '\t']);
        if (end >= 0) token = token[..end];
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}

public sealed class OwnerRecoveryChallenge
{
    public const string PurposeValue = "owner-recovery-challenge";

    public int V { get; set; } = 1;
    public string Purpose { get; set; } = PurposeValue;
    public Guid RequestId { get; set; }
    public string InstallationId { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string? GymName { get; set; }
    public string Nonce { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
}
