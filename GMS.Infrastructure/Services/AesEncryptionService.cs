namespace GMS.Infrastructure.Services;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using GMS.Core.Interfaces;

/// <summary>
/// AES-256-CBC encryption for sensitive data (NationalId, etc.).
/// Key stored in user-secrets: EncryptionKey.
/// </summary>
public class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public AesEncryptionService(IConfiguration config)
    {
        var keyString = config["EncryptionKey"];

        if (string.IsNullOrWhiteSpace(keyString))
        {
            // Hardening (REM-F2): never fall back to a known hardcoded key in Production/Staging.
            // Non-production environments (Development, test hosts, unknown) keep the historical
            // dev fallback so local development and the existing test suite continue unchanged.
            var env = config["ASPNETCORE_ENVIRONMENT"]
                      ?? config["DOTNET_ENVIRONMENT"]
                      ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                      ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

            if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase)
                || string.Equals(env, "Staging", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "EncryptionKey is not configured. National ID encryption requires a 32+ character key " +
                    "from secure configuration (environment variable / secret storage). Refusing to start with a default key.");
            }

            keyString = DevelopmentFallbackKey; // Explicitly NOT used in Production/Staging.
        }

        _key = Encoding.UTF8.GetBytes(keyString.PadRight(32)[..32]);
    }

    /// <summary>
    /// Development-only fallback (32 chars = 256 bits). Never used when the environment is
    /// Production or Staging; an unknown/empty environment also fails closed.
    /// </summary>
    internal const string DevelopmentFallbackKey = "GymFlowPro-AES256-DefaultKey-32C";

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to ciphertext for decryption
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;

        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = _key;

        // Extract IV (first 16 bytes)
        var iv = new byte[16];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
        aes.IV = iv;

        var cipherBytes = new byte[fullCipher.Length - 16];
        Buffer.BlockCopy(fullCipher, 16, cipherBytes, 0, cipherBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
