namespace GMS.Application.Services;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using GMS.Application.Interfaces;

/// <inheritdoc cref="IGymQrTokenService"/>
public class GymQrTokenService : IGymQrTokenService
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(45);

    // Small clock-skew allowance between the machine that minted the token and the one validating it.
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromSeconds(5);

    private readonly byte[] _secretKeyBytes;

    public GymQrTokenService(IConfiguration configuration)
    {
        var secretKey = configuration["JwtSettings:SecretKey"]
            ?? throw new InvalidOperationException("JwtSettings:SecretKey is not configured.");
        _secretKeyBytes = Encoding.UTF8.GetBytes(secretKey);
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(string gymCode, TimeSpan? lifetime = null)
    {
        if (string.IsNullOrWhiteSpace(gymCode))
            throw new ArgumentException("Gym code is required.", nameof(gymCode));

        var expiresAtUtc = DateTime.UtcNow.Add(lifetime ?? DefaultLifetime);
        var expUnixSeconds = ((DateTimeOffset)expiresAtUtc).ToUnixTimeSeconds();
        var nonce = RandomNumberGenerator.GetHexString(16);

        var payload = $"{gymCode}:{expUnixSeconds}:{nonce}";
        var signature = Sign(payload);

        var token = Base64UrlEncode(Encoding.UTF8.GetBytes(payload)) + "." + Base64UrlEncode(signature);
        return (token, expiresAtUtc);
    }

    public GymQrTokenValidationResult Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return GymQrTokenValidationResult.Failure("Invalid or expired QR / رمز QR غير صالح أو منتهي");

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
            return GymQrTokenValidationResult.Failure("Invalid or expired QR / رمز QR غير صالح أو منتهي");

        byte[] payloadBytes, signatureBytes;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            signatureBytes = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return GymQrTokenValidationResult.Failure("Invalid or expired QR / رمز QR غير صالح أو منتهي");
        }

        var payload = Encoding.UTF8.GetString(payloadBytes);
        var expectedSignature = Sign(payload);
        if (!CryptographicOperations.FixedTimeEquals(signatureBytes, expectedSignature))
            return GymQrTokenValidationResult.Failure("Invalid or expired QR / رمز QR غير صالح أو منتهي");

        var segments = payload.Split(':', 3);
        if (segments.Length != 3
            || string.IsNullOrEmpty(segments[0])
            || !long.TryParse(segments[1], out var expUnixSeconds))
        {
            return GymQrTokenValidationResult.Failure("Invalid or expired QR / رمز QR غير صالح أو منتهي");
        }

        var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expUnixSeconds).UtcDateTime;
        if (DateTime.UtcNow > expiresAtUtc.Add(ClockSkewTolerance))
            return GymQrTokenValidationResult.Failure("Invalid or expired QR / رمز QR غير صالح أو منتهي");

        return GymQrTokenValidationResult.Success(segments[0]);
    }

    private byte[] Sign(string payload) => HMACSHA256.HashData(_secretKeyBytes, Encoding.UTF8.GetBytes(payload));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
