namespace GMS.Infrastructure.Services;

using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using GMS.Core.Interfaces;
using GMS.Core.Licensing;

/// <summary>
/// Verifies HyMotion Local license signatures using the embedded ECDSA public key. This is the
/// ONLY licensing key material that ever exists inside a Local installation - the matching
/// private key (GMS.Platform.Services.LicenseSigningService) never runs here.
/// The public key ships in appsettings.Local.json (LicenseVerification:PublicKeyPem) - safe to
/// commit/distribute, since a public key cannot be used to forge a license, only to check one.
/// </summary>
public class LicenseVerificationService : ILicenseVerificationService
{
    private readonly ECDsa? _publicKey;

    public LicenseVerificationService(IConfiguration configuration)
    {
        var pem = configuration["LicenseVerification:PublicKeyPem"];
        if (string.IsNullOrWhiteSpace(pem))
        {
            // Not configured (e.g. SaaS, which never loads this service's consumers anyway) -
            // fail closed rather than throw at DI-construction time, since this service is
            // registered unconditionally for simplicity (see InfrastructureServiceExtensions).
            _publicKey = null;
            return;
        }

        _publicKey = ECDsa.Create();
        _publicKey.ImportFromPem(pem);
    }

    public bool Verify(LocalLicensePayload payload)
    {
        if (payload == null) return false;
        return Verify(LicenseCanonicalizer.Build(payload), payload.Signature);
    }

    public bool Verify(byte[] data, string base64Signature)
    {
        if (_publicKey == null) return false;
        if (data == null || string.IsNullOrWhiteSpace(base64Signature)) return false;

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(base64Signature);
        }
        catch (FormatException)
        {
            return false;
        }

        return _publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256);
    }
}
