namespace GMS.Core.Constants;

public static class EntitlementQuotaPeriods
{
    public const string CairoMonth = "cairo_month";
    public const string Membership = "membership";
    public const string OneTime = "one_time";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        CairoMonth, Membership, OneTime
    };
}
