namespace GMS.Core.Interfaces;

using GMS.Core.Licensing;

/// <summary>
/// Local-side (client) license signature verification. Holds only the ECDSA PUBLIC key - safe to
/// ship inside HyMotion Local, unlike ILicenseSigningService (GMS.Platform), which holds the
/// private key and never runs on a customer's machine.
/// </summary>
public interface ILicenseVerificationService
{
    /// <summary>True only if payload.Signature verifies against the embedded public key for
    /// every other field in payload exactly as given - any tampering with any field (device
    /// limit, installation id, edition, ...) after signing makes this return false.</summary>
    bool Verify(LocalLicensePayload payload);

    /// <summary>True only if <paramref name="base64Signature"/> verifies over
    /// <paramref name="data"/> with the embedded public key. Used for owner-recovery approvals.</summary>
    bool Verify(byte[] data, string base64Signature);
}
