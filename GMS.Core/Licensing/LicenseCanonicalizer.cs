namespace GMS.Core.Licensing;

using System.Globalization;
using System.Text;

/// <summary>
/// Produces the exact byte sequence that gets signed/verified for a LocalLicensePayload. A fixed,
/// manually-ordered delimited string rather than JSON serialization - JSON property order/casing
/// is not a stable contract across .NET versions or serializer settings, and a signature must
/// verify identically forever, including against installs built with a different SDK patch
/// version years from now.
/// </summary>
public static class LicenseCanonicalizer
{
    private const char Sep = '|';

    /// <summary>Everything except Signature itself, in a fixed order. Both the signer (platform)
    /// and the verifier (Local) must call this on the same field values and get byte-identical
    /// output, or the signature will never match.</summary>
    public static byte[] Build(LocalLicensePayload payload)
    {
        var s = string.Join(Sep, new[]
        {
            payload.LicenseId.ToString("D", CultureInfo.InvariantCulture),
            payload.LicenseKey ?? string.Empty,
            payload.Product ?? string.Empty,
            payload.Edition ?? string.Empty,
            payload.InstallationId ?? string.Empty,
            payload.DeviceLimit.ToString(CultureInfo.InvariantCulture),
            payload.IssuedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            payload.LicenseVersion.ToString(CultureInfo.InvariantCulture),
        });
        return Encoding.UTF8.GetBytes(s);
    }
}
