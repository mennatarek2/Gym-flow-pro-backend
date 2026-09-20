namespace GMS.Platform.Helpers;

/// <summary>
/// Server-side mask for Local license secrets on list/detail GET.
/// Full key is returned only when the caller is allowed to reveal (Ops+/Admin)
/// or on Issue/Activate paths that return the key once from the entity.
/// Shape matches FE <c>displayLicenseKey</c> (prefix…suffix).
/// </summary>
public static class MaskedLicenseKey
{
    public static string RevealOrMask(string? licenseKey, bool includeFullKey)
    {
        var value = (licenseKey ?? string.Empty).Trim();
        if (includeFullKey) return value;
        return Mask(value);
    }

    public static string Mask(string? licenseKey)
    {
        var value = (licenseKey ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;
        if (value.Length <= 8) return "••••";
        return $"{value[..6]}…{value[^4..]}";
    }
}
