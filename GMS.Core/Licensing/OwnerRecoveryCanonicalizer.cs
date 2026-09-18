namespace GMS.Core.Licensing;

using System.Globalization;
using System.Text;

/// <summary>
/// Pipe-delimited canonical bytes for owner-recovery signatures. Same contract as
/// <see cref="LicenseCanonicalizer"/>: never JSON, never include the signature itself.
/// </summary>
public static class OwnerRecoveryCanonicalizer
{
    private const char Sep = '|';

    public static byte[] Build(OwnerRecoveryPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var s = string.Join(Sep, new[]
        {
            payload.Purpose ?? string.Empty,
            payload.RequestId.ToString("D", CultureInfo.InvariantCulture),
            payload.InstallationId ?? string.Empty,
            payload.GymCode ?? string.Empty,
            payload.Nonce ?? string.Empty,
            payload.Jti.ToString("D", CultureInfo.InvariantCulture),
            payload.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture),
        });
        return Encoding.UTF8.GetBytes(s);
    }
}
