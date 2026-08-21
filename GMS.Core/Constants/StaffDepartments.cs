namespace GMS.Core.Constants;

/// <summary>
/// Controlled department labels for staff ops (not an HR org chart).
/// </summary>
public static class StaffDepartments
{
    public const string FrontDesk = "Front Desk";
    public const string Sales = "Sales";
    public const string Training = "Training";
    public const string Management = "Management";
    public const string Operations = "Operations";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = new[]
    {
        FrontDesk, Sales, Training, Management, Operations, Other
    };

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim();
        foreach (var item in All)
        {
            if (string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }
}
