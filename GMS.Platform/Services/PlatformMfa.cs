namespace GMS.Platform.Services;

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GMS.Platform.Interfaces;

/// <summary>
/// Dev/local MFA secret store. Persists an encrypted payload as <c>local:...</c>.
/// Production should switch to Key Vault and store <c>kv://...</c> references instead.
/// </summary>
public class LocalEncryptedPlatformMfaSecretStore : IPlatformMfaSecretStore
{
    private const string LocalPrefix = "local:";
    private const string KvPrefix = "kv://";

    private readonly IDataProtector _protector;
    private readonly ILogger<LocalEncryptedPlatformMfaSecretStore> _logger;
    private readonly IConfiguration _configuration;

    public LocalEncryptedPlatformMfaSecretStore(
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        ILogger<LocalEncryptedPlatformMfaSecretStore> logger)
    {
        _protector = dataProtectionProvider.CreateProtector("GMS.Platform.MfaSecret.v1");
        _configuration = configuration;
        _logger = logger;
    }

    public string StoreSecret(Guid userId, string rawBase32Secret)
    {
        // Prefer Key Vault reference mode when configured.
        var kvUri = _configuration["PlatformMfa:KeyVaultUri"];
        if (!string.IsNullOrWhiteSpace(kvUri))
        {
            // Caller is expected to have written the secret to KV; we only store the reference.
            return $"{KvPrefix.TrimEnd('/')}/{userId:N}";
        }

        var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(rawBase32Secret));
        return LocalPrefix + Convert.ToBase64String(protectedBytes);
    }

    public string? ResolveSecret(string? storedReference)
    {
        if (string.IsNullOrWhiteSpace(storedReference))
            return null;

        if (storedReference.StartsWith(LocalPrefix, StringComparison.Ordinal))
        {
            try
            {
                var raw = Convert.FromBase64String(storedReference[LocalPrefix.Length..]);
                return Encoding.UTF8.GetString(_protector.Unprotect(raw));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unprotect local MFA secret reference");
                return null;
            }
        }

        if (storedReference.StartsWith(KvPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Key Vault MFA secret resolution is not wired in this environment for {Ref}",
                storedReference);
            return null;
        }

        return null;
    }
}

public static class PlatformTotp
{
    public static string GenerateSecret()
    {
        var bytes = new byte[20];
        RandomNumberGenerator.Fill(bytes);
        return Base32Encode(bytes);
    }

    public static bool Verify(string base32Secret, string code, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        code = code.Trim().Replace(" ", "", StringComparison.Ordinal);
        var key = Base32Decode(base32Secret);
        var totp = new OtpNet.Totp(key);
        return totp.VerifyTotp(code, out _, new OtpNet.VerificationWindow(window, window));
    }

    public static string BuildOtpAuthUri(string email, string base32Secret, string issuer = "GymFlow Platform")
    {
        var label = Uri.EscapeDataString($"{issuer}:{email}");
        var iss = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={iss}&digits=6&period=30";
    }

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = data[0];
        int next = 1;
        int bitsLeft = 8;
        while (bitsLeft > 0 || next < data.Length)
        {
            if (bitsLeft < 5)
            {
                if (next < data.Length)
                {
                    buffer <<= 8;
                    buffer |= data[next++] & 0xff;
                    bitsLeft += 8;
                }
                else
                {
                    int pad = 5 - bitsLeft;
                    buffer <<= pad;
                    bitsLeft += pad;
                }
            }

            int index = (buffer >> (bitsLeft - 5)) & 0x1f;
            bitsLeft -= 5;
            output.Append(alphabet[index]);
        }

        return output.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>();
        int buffer = 0;
        int bitsLeft = 0;
        foreach (var c in input)
        {
            var val = alphabet.IndexOf(c);
            if (val < 0) continue;
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xff));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }
}
