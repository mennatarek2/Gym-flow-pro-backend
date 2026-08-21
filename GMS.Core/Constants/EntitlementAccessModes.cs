namespace GMS.Core.Constants;

public static class EntitlementAccessModes
{
    public const string Included = "included";
    public const string Unlimited = "unlimited";
    public const string Limited = "limited";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Included, Unlimited, Limited
    };
}
