namespace GMS.Platform.Services;

using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using GMS.Core.Licensing;

public interface ILicenseSigningService
{
    /// <summary>Signs everything in <paramref name="payload"/> except Signature and returns the
    /// base64 signature - caller sets payload.Signature = result before shipping it to a client.
    /// Never returns/logs/exposes the private key itself.</summary>
    string Sign(LocalLicensePayload payload);

    /// <summary>Signs arbitrary canonical bytes with the same ECDSA P-256 key used for licenses.
    /// Used for owner-recovery approvals. Never returns/logs the private key.</summary>
    string Sign(byte[] data);
}

/// <summary>
/// Signs HyMotion Local license payloads with the platform's ECDSA (P-256) private key. This is
/// the ONLY place that key is ever loaded into memory - it never leaves this process, is never
/// returned from an API, and is never written to a log (rule 12: private key must never reach a
/// Local installation, a Sales Rep, or an API response).
///
/// Key storage follows the exact same convention as JwtSettings:SecretKey (PlatformTokenService):
/// read from IConfiguration, checked-in appsettings files carry an empty placeholder, the real
/// value lives in dotnet user-secrets (dev) / environment variables or the hosting platform's
/// secret store (production) - see docs/local/LOCAL_LICENSING.md "Key Management". No real Key
/// Vault integration exists anywhere in this repo today (verified by repo-wide search before
/// building this), so this does not pretend to use one either.
/// </summary>
public class LicenseSigningService : ILicenseSigningService
{
    private readonly ECDsa _privateKey;

    public LicenseSigningService(IConfiguration configuration)
    {
        var pem = configuration["LicenseSigning:PrivateKeyPem"];
        if (string.IsNullOrWhiteSpace(pem))
            throw new InvalidOperationException("LicenseSigning:PrivateKeyPem is not configured.");

        _privateKey = ECDsa.Create();
        _privateKey.ImportFromPem(pem);
    }

    public string Sign(LocalLicensePayload payload) => Sign(LicenseCanonicalizer.Build(payload));

    public string Sign(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var signature = _privateKey.SignData(data, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(signature);
    }
}
